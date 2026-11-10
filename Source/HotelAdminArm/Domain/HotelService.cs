using System.Data;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HotelAdminArm.Data;
using Microsoft.Data.Sqlite;

namespace HotelAdminArm.Domain;

internal sealed record BillLine(int StayId,string Description,int Quantity,long UnitKopecks)
{
    public long Total => checked(Quantity*UnitKopecks);
}
internal sealed record Bill(IReadOnlyList<BillLine> Lines,string Fingerprint)
{
    public long Total => Lines.Aggregate(0L,(total,line)=>checked(total+line.Total));
    public string Text => string.Join(Environment.NewLine,Lines.Select(l=>$"Проживание #{l.StayId}: {l.Description}\n  {l.Quantity} × {HotelService.Rubles(l.UnitKopecks):N2} = {HotelService.Rubles(l.Total):N2} руб."))+
        $"\n\nИТОГО: {HotelService.Rubles(Total):N2} руб.";
}

internal static class HotelService
{
    public static long Kopecks(decimal value) => checked((long)decimal.Round(value*100,0,MidpointRounding.AwayFromZero));
    public static decimal Rubles(long value) => value/100m;
    public static string Iso(DateTime date) => date.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    public static string NormalizePassport(string passport) => string.Concat(passport.Where(ch=>!char.IsWhiteSpace(ch) && ch!='-')).ToUpperInvariant();
    public static string SuggestedMealKind(string name)=>name.Trim().ToLowerInvariant() switch{"завтрак"=>"Breakfast","обед"=>"Lunch","ужин"=>"Dinner",_=>"None"};
    // Единые границы приёма заказов используют форма, бизнес-слой и функции SQLite.
    public static TimeSpan MealDeadline(string mealKind) => TimeSpan.FromHours(mealKind switch
    {
        "Breakfast" => 10, "Lunch" => 14, "Dinner" => 20,
        _ => throw new ArgumentException("Неизвестная категория питания.",nameof(mealKind))
    });
    private static T Atomic<T>(Func<SqliteConnection,SqliteTransaction,T> action)
    {
        using var c=Database.OpenConnection(); using var tx=c.BeginTransaction();
        var result=action(c,tx); tx.Commit(); return result;
    }
    private static DataRow Row(SqliteConnection c,SqliteTransaction? tx,string sql,params SqliteParameter[] parameters)
    {
        var data=Database.Read(c,tx,sql,parameters);
        return data.Rows.Count>0 ? data.Rows[0] : throw new InvalidOperationException("Запись не найдена. Обновите список.");
    }
    private static long N(DataRow row,string column) => Convert.ToInt64(row[column]);
    private static string S(DataRow row,string column) => Convert.ToString(row[column])??"";
    private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private static void ActiveGuest(SqliteConnection c,SqliteTransaction tx,int id) => Require(Database.Scalar<long>(c,tx,"SELECT count(*) FROM Guests WHERE Id=@id AND IsActive=1",Database.P("id",id))==1,"Выберите активного гостя.");
    public static void ValidatePeriod(DateTime from,DateTime to,bool allowPast=false)
    {
        Require(to.Date>from.Date,"Дата выезда должна быть позже даты заезда.");
        Require(allowPast || from.Date>=Database.Today,"Для новой брони выберите сегодняшнюю или будущую дату.");
    }

    // Search, calendar and command validation use the same half-open interval rule.
    internal const string AvailableSql="""
        SELECT r.Id,r.Number AS 'Номер',rt.Name AS 'Тип',r.Floor AS 'Этаж',r.Capacity AS 'Мест',
            r.PriceKopecks/100.0 AS 'Цена за сутки'
        FROM Rooms r JOIN RoomTypes rt ON rt.Id=r.RoomTypeId
        WHERE r.IsActive=1 AND r.UnderMaintenance=0 AND (@type=0 OR r.RoomTypeId=@type) AND r.Capacity>=@capacity
            AND NOT EXISTS(SELECT 1 FROM Bookings b WHERE b.RoomId=r.Id AND b.Id<>@booking AND b.Status IN('Новое','Подтверждено')
                AND @from<b.DateTo AND @to>b.DateFrom)
            AND NOT EXISTS(SELECT 1 FROM Stays s WHERE s.RoomId=r.Id AND s.Id<>@stay AND s.Status='Проживает'
                AND @to>s.CheckInDate AND (s.PlannedCheckOutDate<=hotel_today() OR @from<s.PlannedCheckOutDate))
        ORDER BY r.Number
        """;
    private static SqliteParameter[] AvailabilityParameters(DateTime from,DateTime to,int type,int capacity,int booking,int stay) => new[]{
        Database.P("from",from),Database.P("to",to),Database.P("type",type),Database.P("capacity",capacity),Database.P("booking",booking),Database.P("stay",stay)};
    public static DataTable Available(DateTime from,DateTime to,int type=0,int capacity=1)
    {
        ValidatePeriod(from,to);
        return Database.Query(AvailableSql,AvailabilityParameters(from,to,type,capacity,0,0));
    }
    private static void CheckAvailable(SqliteConnection c,SqliteTransaction tx,int room,DateTime from,DateTime to,int booking=0,int stay=0,int capacity=1)
    {
        var available=Database.Read(c,tx,AvailableSql,AvailabilityParameters(from,to,0,capacity,booking,stay));
        Require(available.AsEnumerable().Any(r=>Convert.ToInt32(r["Id"])==room),"Номер недоступен: ремонт, вместимость, бронь или незавершённое проживание.");
    }

    public static DataTable Rooms() => Database.Query("""
        SELECT r.Id,r.Number AS 'Номер',rt.Name AS 'Тип',r.Floor AS 'Этаж',r.Capacity AS 'Мест',r.PriceKopecks/100.0 AS 'Цена за сутки',
            CASE WHEN r.IsActive=0 THEN 'Неактивен' WHEN r.UnderMaintenance=1 THEN 'Ремонт'
                WHEN EXISTS(SELECT 1 FROM Stays s WHERE s.RoomId=r.Id AND s.Status='Проживает' AND s.PlannedCheckOutDate<=hotel_today()) THEN 'Занят — выезд просрочен'
                WHEN EXISTS(SELECT 1 FROM Stays s WHERE s.RoomId=r.Id AND s.Status='Проживает') THEN 'Занят'
                WHEN EXISTS(SELECT 1 FROM Bookings b WHERE b.RoomId=r.Id AND b.Status IN('Новое','Подтверждено') AND b.DateFrom<=hotel_today() AND b.DateTo>hotel_today()) THEN 'Забронирован'
                ELSE 'Свободен' END AS 'Сейчас',
            COALESCE((SELECT group_concat(b.DateFrom||' — '||b.DateTo,'; ') FROM Bookings b WHERE b.RoomId=r.Id AND b.Status IN('Новое','Подтверждено') AND b.DateTo>hotel_today()),'') AS 'Брони',r.Notes AS 'Примечание'
        FROM Rooms r JOIN RoomTypes rt ON rt.Id=r.RoomTypeId ORDER BY r.Number
        """);

    public static int SaveRoom(int id,string number,int type,int floor,int capacity,decimal price,bool repair,bool active,string notes,byte[]? photo) => Atomic((c,tx)=>
    {
        Require(!string.IsNullOrWhiteSpace(number),"Введите номер комнаты.");
        Require(type>0 && floor>=1 && floor<=50 && capacity>=1 && capacity<=10,"Проверьте тип, этаж и вместимость.");
        Require(price>=0 && price<=1000000,"Некорректная цена.");
        var p=new[]{Database.P("id",id),Database.P("number",number.Trim()),Database.P("type",type),Database.P("floor",floor),Database.P("capacity",capacity),
            Database.P("price",price),Database.P("kopecks",Kopecks(price)),Database.P("repair",repair?1:0),Database.P("active",active?1:0),Database.P("status",repair?"Ремонт":"Свободен"),Database.P("notes",notes.Trim()),Database.P("photo",photo)};
        if(id==0) return checked((int)Database.Insert(c,tx,"INSERT INTO Rooms(Number,RoomTypeId,Floor,Capacity,DailyPrice,PriceKopecks,Status,UnderMaintenance,IsActive,Notes,Photo) VALUES(@number,@type,@floor,@capacity,@price,@kopecks,@status,@repair,@active,@notes,@photo)",p));
        Require(Database.Execute(c,tx,"UPDATE Rooms SET Number=@number,RoomTypeId=@type,Floor=@floor,Capacity=@capacity,DailyPrice=@price,PriceKopecks=@kopecks,Status=@status,UnderMaintenance=@repair,IsActive=@active,Notes=@notes,Photo=@photo WHERE Id=@id",p)==1,"Номер удалён другим пользователем.");
        return id;
    });

    public static int SaveGuest(int id,string last,string first,string middle,DateTime? birth,string passport,string phone,string email,string address,bool active) => Atomic((c,tx)=>
    {
        Require(!string.IsNullOrWhiteSpace(last) && !string.IsNullOrWhiteSpace(first),"Введите фамилию и имя.");
        var key=NormalizePassport(passport); Require(key.Length>0 && key.All(char.IsLetterOrDigit),"Введите паспорт: буквы, цифры, пробелы или дефисы.");
        Require(!birth.HasValue || birth.Value.Date<=Database.Today,"Дата рождения не может быть в будущем.");
        Require(string.IsNullOrWhiteSpace(phone) || Regex.IsMatch(phone,@"^\+?[\d\s()\-]{5,25}$") && phone.Count(char.IsDigit)>=5,"Проверьте номер телефона.");
        Require(string.IsNullOrWhiteSpace(email) || MailAddress.TryCreate(email.Trim(),out var mail) && mail.Address==email.Trim(),"Проверьте email.");
        if(!active && id>0) Require(Database.Scalar<long>(c,tx,"SELECT count(*) FROM Stays WHERE GuestId=@id AND Status='Проживает'",Database.P("id",id))==0 && Database.Scalar<long>(c,tx,"SELECT count(*) FROM Bookings WHERE GuestId=@id AND Status IN('Новое','Подтверждено') AND DateTo>hotel_today()",Database.P("id",id))==0,"У гостя есть проживание или будущая бронь.");
        var p=new[]{Database.P("id",id),Database.P("last",last.Trim()),Database.P("first",first.Trim()),Database.P("middle",middle.Trim()),Database.P("birth",birth),Database.P("passport",passport.Trim()),Database.P("key",key),Database.P("phone",phone.Trim()),Database.P("email",email.Trim()),Database.P("address",address.Trim()),Database.P("active",active?1:0)};
        if(id==0)return checked((int)Database.Insert(c,tx,"INSERT INTO Guests(LastName,FirstName,MiddleName,BirthDate,Passport,PassportKey,Phone,Email,Address,IsActive) VALUES(@last,@first,@middle,@birth,@passport,@key,@phone,@email,@address,@active)",p));
        Require(Database.Execute(c,tx,"UPDATE Guests SET LastName=@last,FirstName=@first,MiddleName=@middle,BirthDate=@birth,Passport=@passport,PassportKey=@key,Phone=@phone,Email=@email,Address=@address,IsActive=@active WHERE Id=@id",p)==1,"Гость не найден."); return id;
    });

    public static int SaveBooking(int id,int guest,int room,DateTime from,DateTime to,string status,string comment) => Atomic((c,tx)=>
    {
        ValidatePeriod(from,to); ActiveGuest(c,tx,guest);Require(room>0,"Выберите номер для брони.");
        Require(status is "Новое" or "Подтверждено","Используйте кнопки отмены и заселения для изменения статуса.");
        if(id>0) Require(S(Row(c,tx,"SELECT * FROM Bookings WHERE Id=@id",Database.P("id",id)),"Status") is "Новое" or "Подтверждено","Завершённая бронь защищена.");
        CheckAvailable(c,tx,room,from,to,id);
        var rate=Database.Scalar<long>(c,tx,"SELECT PriceKopecks FROM Rooms WHERE Id=@id",Database.P("id",room));
        // An unchanged room keeps its agreed tariff when dates or comments are edited.
        if(id>0){var old=Row(c,tx,"SELECT RoomId,RateKopecks FROM Bookings WHERE Id=@id",Database.P("id",id)); if(N(old,"RoomId")==room)rate=N(old,"RateKopecks");}
        var p=new[]{Database.P("id",id),Database.P("guest",guest),Database.P("room",room),Database.P("from",from),Database.P("to",to),Database.P("status",status),Database.P("comment",comment.Trim()),Database.P("rate",rate)};
        if(id==0)return checked((int)Database.Insert(c,tx,"INSERT INTO Bookings(GuestId,RoomId,DateFrom,DateTo,Status,Comment,RateKopecks) VALUES(@guest,@room,@from,@to,@status,@comment,@rate)",p));
        Database.Execute(c,tx,"UPDATE Bookings SET GuestId=@guest,RoomId=@room,DateFrom=@from,DateTo=@to,Status=@status,Comment=@comment,RateKopecks=@rate,IsLegacyEstimated=CASE WHEN RoomId=@room THEN IsLegacyEstimated ELSE 0 END WHERE Id=@id",p);return id;
    });
    public static void CancelBooking(int id) => Atomic((c,tx)=>
    {
        Require(Database.Execute(c,tx,"UPDATE Bookings SET Status='Отменено' WHERE Id=@id AND Status IN('Новое','Подтверждено')",Database.P("id",id))==1,"Выберите незаселённую активную бронь.");return 0;
    });
    public static void DeleteBooking(int id)=>Database.Execute("DELETE FROM Bookings WHERE Id=@id",Database.P("id",id));

    public static int CheckIn(int guest,int room,DateTime plannedOut,string notes,int? booking=null) => Atomic((c,tx)=>
    {
        long rate; var estimated=0;
        if(booking.HasValue)
        {
            var b=Row(c,tx,"SELECT * FROM Bookings WHERE Id=@id",Database.P("id",booking.Value));
            Require(S(b,"Status")=="Подтверждено" && DateTime.Parse(S(b,"DateFrom"))<=Database.Today && DateTime.Parse(S(b,"DateTo"))>Database.Today,"Бронь должна быть подтверждена и действовать сегодня.");
            guest=(int)N(b,"GuestId");room=(int)N(b,"RoomId");plannedOut=DateTime.Parse(S(b,"DateTo"));rate=N(b,"RateKopecks");estimated=(int)N(b,"IsLegacyEstimated");
        }
        else rate=Database.Scalar<long>(c,tx,"SELECT PriceKopecks FROM Rooms WHERE Id=@id",Database.P("id",room));
        ValidatePeriod(Database.Today,plannedOut);ActiveGuest(c,tx,guest);
        CheckAvailable(c,tx,room,Database.Today,plannedOut,booking??0);
        Require(Database.Scalar<long>(c,tx,"SELECT count(*) FROM Stays WHERE RoomId=@id AND Status='Проживает'",Database.P("id",room))==0,"Предыдущий гость ещё не выехал.");
        return checked((int)Database.Insert(c,tx,"INSERT INTO Stays(GuestId,RoomId,BookingId,CheckInDate,PlannedCheckOutDate,Status,Notes,RateKopecks,IsLegacyEstimated) VALUES(@guest,@room,@booking,@today,@out,'Проживает',@notes,@rate,@legacy)",
            Database.P("guest",guest),Database.P("room",room),Database.P("booking",booking),Database.P("today",Database.Today),Database.P("out",plannedOut),Database.P("notes",notes.Trim()),Database.P("rate",rate),Database.P("legacy",estimated)));
    });
    private static DataRow ActiveStay(SqliteConnection c,SqliteTransaction tx,int id)
    {
        var s=Row(c,tx,"SELECT * FROM Stays WHERE Id=@id",Database.P("id",id));Require(S(s,"Status")=="Проживает","Проживание уже завершено. Повторная оплата запрещена.");return s;
    }
    public static void Extend(int id,DateTime to)=>Atomic((c,tx)=>
    {
        var s=ActiveStay(c,tx,id);Require(to.Date>Database.Today && to.Date>DateTime.Parse(S(s,"PlannedCheckOutDate")),"Новая дата должна быть позже текущей даты и прежнего планового выезда.");
        CheckAvailable(c,tx,(int)N(s,"RoomId"),Database.Today,to,s["BookingId"]==DBNull.Value?0:(int)N(s,"BookingId"),id);
        Database.Execute(c,tx,"UPDATE Stays SET PlannedCheckOutDate=@to WHERE Id=@id",Database.P("to",to),Database.P("id",id));return 0;
    });
    public static void Move(int id,int room)=>Atomic((c,tx)=>
    {
        var s=ActiveStay(c,tx,id);Require(N(s,"RoomId")!=room,"Выберите другой номер.");
        var to=DateTime.Parse(S(s,"PlannedCheckOutDate"));Require(to>Database.Today,"Сначала продлите просроченное проживание.");
        var guests=Database.Scalar<int>(c,tx,"SELECT count(*) FROM StayGuests WHERE StayId=@id",Database.P("id",id));
        CheckAvailable(c,tx,room,Database.Today,to,0,id,guests);
        Require(Database.Scalar<long>(c,tx,"SELECT count(*) FROM Stays WHERE RoomId=@room AND Status='Проживает'",Database.P("room",room))==0,"Новый номер ещё занят.");
        Database.Execute(c,tx,"UPDATE StaySegments SET DateTo=@today WHERE StayId=@id AND DateTo IS NULL",Database.P("today",Database.Today),Database.P("id",id));
        Database.Execute(c,tx,"INSERT INTO StaySegments(StayId,RoomId,RoomNumber,DateFrom,RateKopecks) SELECT @stay,Id,Number,@today,PriceKopecks FROM Rooms WHERE Id=@room",Database.P("stay",id),Database.P("room",room),Database.P("today",Database.Today));
        Database.Execute(c,tx,"UPDATE Stays SET RoomId=@room WHERE Id=@id",Database.P("room",room),Database.P("id",id));return 0;
    });
    public static void AddOccupant(int stay,int guest)=>Atomic((c,tx)=>{ActiveStay(c,tx,stay);ActiveGuest(c,tx,guest);Database.Execute(c,tx,"INSERT INTO StayGuests VALUES(@stay,@guest)",Database.P("stay",stay),Database.P("guest",guest));return 0;});
    public static void RemoveOccupant(int stay,int guest)=>Database.Execute("DELETE FROM StayGuests WHERE StayId=@stay AND GuestId=@guest",Database.P("stay",stay),Database.P("guest",guest));
    public static int SaveService(int id,string name,decimal price,bool active,string mealKind="None",string repeatRule="Repeatable")=>Atomic((c,tx)=>
    {
        Require(!string.IsNullOrWhiteSpace(name),"Введите название услуги.");Require(price>=0 && price<=1000000,"Некорректная цена услуги.");
        Require(new[]{"None","Breakfast","Lunch","Dinner"}.Contains(mealKind),"Выберите категорию услуги.");
        Require(SuggestedMealKind(name)=="None" || SuggestedMealKind(name)==mealKind,"Выберите соответствующую названию категорию питания.");
        Require(new[]{"Repeatable","Daily","Once"}.Contains(repeatRule),"Выберите правило повторения.");
        Require(mealKind=="None" || repeatRule=="Daily","Питание разрешено один раз в день на гостя.");
        var p=new[]{Database.P("id",id),Database.P("name",name.Trim()),Database.P("price",price),Database.P("k",Kopecks(price)),Database.P("active",active?1:0),Database.P("meal",mealKind),Database.P("rule",repeatRule)};
        if(id==0)return checked((int)Database.Insert(c,tx,"INSERT INTO Services(Name,Price,PriceKopecks,IsActive,MealKind,RepeatRule) VALUES(@name,@price,@k,@active,@meal,@rule)",p));
        Require(Database.Execute(c,tx,"UPDATE Services SET Name=@name,Price=@price,PriceKopecks=@k,IsActive=@active,MealKind=@meal,RepeatRule=@rule WHERE Id=@id",p)==1,"Услуга не найдена.");return id;
    });
    public static void AddOrder(int stay,int service,int quantity,int? guest=null,DateTime? mealDate=null)=>Atomic((c,tx)=>
    {
        var stayRow=ActiveStay(c,tx,stay);Require(quantity is >=1 and <=100,"Количество должно быть от 1 до 100.");
        var catalog=Row(c,tx,"SELECT * FROM Services WHERE Id=@id AND IsActive=1",Database.P("id",service));
        var meal=S(catalog,"MealKind");var rule=S(catalog,"RepeatRule");string guestName="";
        // OrderDate хранит день предоставления питания, а не день оформления будущего заказа.
        // Для обычных услуг сохраняется текущая дата. Границы повторно проверяются при INSERT.
        var orderDate=meal=="None"?Database.Today:(mealDate??Database.Today).Date;
        if(meal!="None")
        {
            Require(quantity==1,"Одна порция питания на гостя.");
            Require(orderDate>=Database.Today && orderDate>=DateTime.Parse(S(stayRow,"CheckInDate")) && orderDate<DateTime.Parse(S(stayRow,"PlannedCheckOutDate")),
                "Выберите дату питания от сегодня до дня перед выездом; при необходимости продлите проживание.");
            Require(orderDate!=Database.Today || Database.Now.TimeOfDay<MealDeadline(meal),
                $"Приём заказов на это питание сегодня завершён (до {MealDeadline(meal):hh\\:mm}). Выберите следующий день проживания.");
            var person=Row(c,tx,"SELECT g.LastName||' '||g.FirstName||' '||IFNULL(g.MiddleName,'') AS Name FROM StayGuests sg JOIN Guests g ON g.Id=sg.GuestId WHERE sg.StayId=@stay AND sg.GuestId=@guest AND g.IsActive=1",Database.P("stay",stay),Database.P("guest",guest));
            guestName=S(person,"Name");
            Require(Database.Scalar<long>(c,tx,"SELECT count(*) FROM ServiceOrders WHERE MealKind=@meal AND OrderDate=@today AND (GuestId=@guest OR (GuestId IS NULL AND StayId=@stay))",Database.P("meal",meal),Database.P("today",orderDate),Database.P("guest",guest),Database.P("stay",stay))==0,"Это питание уже заказано на выбранную дату. Проверьте также старые заказы.");
        }
        else
        {
            Require(guest is null,"Эта услуга относится к проживанию.");
            if(rule!="Repeatable")
            {
                Require(quantity==1,"Для ограниченной услуги количество должно быть равно 1.");
                Require(Database.Scalar<long>(c,tx,"SELECT count(*) FROM ServiceOrders WHERE StayId=@stay AND ServiceId=@service AND (@rule='Once' OR OrderDate=@today)",Database.P("stay",stay),Database.P("service",service),Database.P("rule",rule),Database.P("today",orderDate))==0,"Лимит услуги для проживания уже использован.");
            }
        }
        Database.Execute(c,tx,"INSERT INTO ServiceOrders(StayId,ServiceId,Quantity,OrderDate,UnitPriceKopecks,NameSnapshot,GuestId,GuestNameSnapshot,MealKind,RepeatRule) VALUES(@stay,@service,@qty,@today,@price,@name,@guest,@guestName,@meal,@rule)",Database.P("stay",stay),Database.P("service",service),Database.P("qty",quantity),Database.P("today",orderDate),Database.P("price",catalog["PriceKopecks"]),Database.P("name",catalog["Name"]),Database.P("guest",guest),Database.P("guestName",guestName),Database.P("meal",meal),Database.P("rule",rule));return 0;
    });
    public static void DeleteOrder(int id)=>Database.Execute("DELETE FROM ServiceOrders WHERE Id=@id",Database.P("id",id));

    public static Bill Preview(params int[] ids)=>Atomic((c,tx)=>BuildBill(c,tx,ids));
    private static Bill BuildBill(SqliteConnection c,SqliteTransaction tx,int[] ids)
    {
        Require(ids.Length>0,"Выберите проживание.");
        var lines=new List<BillLine>(); var fingerprint=new StringBuilder(Iso(Database.Today));
        foreach(var id in ids.Distinct().Order())
        {
            var s=ActiveStay(c,tx,id);Require(DateTime.Parse(S(s,"CheckInDate"))<=Database.Today,"Заезд ещё не состоялся.");
            var guestName=Database.Scalar<string>(c,tx,"SELECT LastName||' '||FirstName||' '||IFNULL(MiddleName,'') FROM Guests WHERE Id=@id",Database.P("id",s["GuestId"]));
            fingerprint.Append($"|{id}:{s["RoomId"]}:{s["PlannedCheckOutDate"]}:{s["GuestId"]}");
            var segments=Database.Read(c,tx,"SELECT * FROM StaySegments WHERE StayId=@id ORDER BY DateFrom,Id",Database.P("id",id));
            Require(segments.Rows.Count>0,"Отсутствует история тарифа проживания.");
            var billedNights=0;
            foreach(DataRow segment in segments.Rows)
            {
                var from=DateTime.Parse(S(segment,"DateFrom"));var to=segment["DateTo"]==DBNull.Value?Database.Today:DateTime.Parse(S(segment,"DateTo"));
                var nights=Math.Max(0,(to.Date-from.Date).Days);
                if(nights>0){lines.Add(new(id,$"{guestName}; номер {S(segment,"RoomNumber")}: {Iso(from)} — {Iso(to)}",nights,N(segment,"RateKopecks")));billedNights+=nights;}
                fingerprint.Append($"|segment:{segment["Id"]}:{segment["DateFrom"]}:{segment["DateTo"]}:{segment["RateKopecks"]}");
            }
            if(billedNights==0){var last=segments.Rows[^1];lines.Add(new(id,$"{guestName}; номер {S(last,"RoomNumber")}: минимальные сутки",1,N(last,"RateKopecks")));}
            foreach(DataRow order in Database.Read(c,tx,"SELECT * FROM ServiceOrders WHERE StayId=@id ORDER BY Id",Database.P("id",id)).Rows)
                lines.Add(new(id,$"{S(order,"NameSnapshot")} ({S(order,"OrderDate")})"+(S(order,"GuestNameSnapshot").Length>0?$" — {S(order,"GuestNameSnapshot")}":""),checked((int)N(order,"Quantity")),N(order,"UnitPriceKopecks")));
            if(N(s,"IsLegacyEstimated")==1) fingerprint.Append("|legacy-estimated");
        }
        foreach(var line in lines)fingerprint.Append($"|{line.StayId}:{line.Description}:{line.Quantity}:{line.UnitKopecks}");
        return new(lines,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()))));
    }
    public static int Checkout(int[] ids,int payer,string method,string expectedFingerprint)=>Atomic((c,tx)=>
    {
        ActiveGuest(c,tx,payer);Require(method is "Наличные" or "Банковская карта" or "Перевод","Выберите способ оплаты.");
        var bill=BuildBill(c,tx,ids);Require(bill.Fingerprint==expectedFingerprint,"Счёт изменился. Рассчитайте и подтвердите его заново.");
        foreach(var id in ids.Distinct())
        {
            Database.Execute(c,tx,"DELETE FROM InvoiceLines WHERE StayId=@id",Database.P("id",id));
            foreach(var line in bill.Lines.Where(l=>l.StayId==id))
                Database.Execute(c,tx,"INSERT INTO InvoiceLines(StayId,Description,Quantity,UnitKopecks,TotalKopecks) VALUES(@id,@text,@qty,@unit,@total)",Database.P("id",id),Database.P("text",line.Description),Database.P("qty",line.Quantity),Database.P("unit",line.UnitKopecks),Database.P("total",line.Total));
        }
        var payment=0;
        if(bill.Total>0)
        {
            payment=checked((int)Database.Insert(c,tx,"INSERT INTO Payments(PayerGuestId,PayerNameSnapshot,PaymentDate,Amount,AmountKopecks,Method,Comment,Finalized) SELECT Id,LastName||' '||FirstName||' '||IFNULL(MiddleName,''),@today,0,0,@method,@comment,0 FROM Guests WHERE Id=@payer",Database.P("payer",payer),Database.P("today",Database.Today),Database.P("method",method),Database.P("comment","Оплата проживаний: "+string.Join(", ",ids.Distinct()))));
            foreach(var id in ids.Distinct())
            {
                var amount=bill.Lines.Where(l=>l.StayId==id).Aggregate(0L,(total,l)=>checked(total+l.Total));
                Database.Execute(c,tx,"INSERT INTO PaymentStays(PaymentId,StayId,Amount,AmountKopecks) VALUES(@payment,@stay,@rubles,@kopecks)",Database.P("payment",payment),Database.P("stay",id),Database.P("rubles",Rubles(amount)),Database.P("kopecks",amount));
            }
            Database.Execute(c,tx,"UPDATE Payments SET Amount=@rubles,AmountKopecks=@kopecks,Finalized=1 WHERE Id=@id",Database.P("rubles",Rubles(bill.Total)),Database.P("kopecks",bill.Total),Database.P("id",payment));
        }
        foreach(var id in ids.Distinct())
        {
            Database.Execute(c,tx,"UPDATE StaySegments SET DateTo=@today WHERE StayId=@id AND DateTo IS NULL",Database.P("today",Database.Today),Database.P("id",id));
            var amount=bill.Lines.Where(l=>l.StayId==id).Aggregate(0L,(total,l)=>checked(total+l.Total));
            Require(Database.Execute(c,tx,"UPDATE Stays SET Status='Выбыл',ActualCheckOutDate=@today,SettlementKopecks=@amount WHERE Id=@id AND Status='Проживает'",Database.P("today",Database.Today),Database.P("amount",amount),Database.P("id",id))==1,"Проживание уже закрыто другим пользователем.");
        }
        return payment;
    });

    public static string ClosedBill(int stay)
    {
        var s=Database.Query("SELECT s.*,COALESCE(a.FullName,g.LastName||' '||g.FirstName) AS FullName FROM Stays s JOIN Guests g ON g.Id=s.GuestId LEFT JOIN GuestArchive a ON a.StayId=s.Id WHERE s.Id=@id",Database.P("id",stay)).Rows.Cast<DataRow>().Single();
        var lines=Database.Query("SELECT * FROM InvoiceLines WHERE StayId=@id ORDER BY Id",Database.P("id",stay)).AsEnumerable().Select(r=>new BillLine(stay,S(r,"Description"),(int)N(r,"Quantity"),N(r,"UnitKopecks"))).ToList();
        var recorded=Database.Scalar<long>("SELECT COALESCE(SUM(AmountKopecks),0) FROM PaymentStays WHERE StayId=@id",Database.P("id",stay));
        return $"{HotelBranding.HotelName}\nПроживание #{stay}\nГость: {S(s,"FullName")}\nЗаезд: {S(s,"CheckInDate")}\nВыезд: {S(s,"ActualCheckOutDate")}\n\n"+
            (lines.Count>0?new Bill(lines,"").Text:$"Историческая запись: детализация не была сохранена.\nЗарегистрировано оплат: {Rubles(recorded):N2} руб.\n"+(s["SettlementKopecks"]==DBNull.Value?"Нет подтверждённого расчёта; требуется сверка.":""))+
            (N(s,"IsLegacyEstimated")==1?"\nСтарые тарифы восстановлены приблизительно; зарегистрированные оплаты сохранены.":"");
    }
    public static string RegistrationCard(int stay)
    {
        Require(stay>0,"Выберите проживание для карточки регистрации.");
        var rows=Database.Query("""
            SELECT s.Id,r.Number,s.CheckInDate,s.PlannedCheckOutDate,g.LastName,g.FirstName,g.MiddleName,g.BirthDate,g.Passport,g.Phone,g.Email,g.Address
            FROM Stays s JOIN Rooms r ON r.Id=s.RoomId JOIN StayGuests sg ON sg.StayId=s.Id JOIN Guests g ON g.Id=sg.GuestId
            WHERE s.Id=@stay ORDER BY g.Id
            """,Database.P("stay",stay));
        Require(rows.Rows.Count>0,"Проживание или проживающие не найдены.");
        return string.Join("\n\n",rows.AsEnumerable().Select(r=>$"{HotelBranding.HotelName}\nКАРТОЧКА РЕГИСТРАЦИИ ГОСТЯ\nПроживание № {stay}, номер {S(r,"Number")}\nФИО: {S(r,"LastName")} {S(r,"FirstName")} {S(r,"MiddleName")}\nДата рождения: {S(r,"BirthDate")}\nПаспорт: {S(r,"Passport")}\nАдрес: {S(r,"Address")}\nТелефон: {S(r,"Phone")}\nEmail: {S(r,"Email")}\nЗаезд: {S(r,"CheckInDate")}\nПлановый выезд: {S(r,"PlannedCheckOutDate")}\nПодпись гостя: ____________________"));
    }

    public static DataTable Warnings()=>Database.Query("""
        SELECT Id,'Проживание' AS 'Раздел','Просрочен плановый выезд — номер остаётся занят' AS 'Проблема' FROM Stays WHERE Status='Проживает' AND PlannedCheckOutDate<=hotel_today()
        UNION ALL SELECT Id,'Бронь','Истёк период; отметьте отмену/неявку' FROM Bookings WHERE Status IN('Новое','Подтверждено') AND DateTo<=hotel_today()
        UNION ALL SELECT Id,'Архив','Не сохранён подтверждённый расчёт' FROM Stays WHERE Status='Выбыл' AND SettlementKopecks IS NULL
        UNION ALL SELECT Id,'Проживание','Старый тариф восстановлен приблизительно' FROM Stays WHERE IsLegacyEstimated=1
        UNION ALL SELECT Id,'Бронь','Старый тариф восстановлен приблизительно' FROM Bookings WHERE IsLegacyEstimated=1
        UNION ALL SELECT Id,'Услуга','Старая цена восстановлена приблизительно' FROM ServiceOrders WHERE IsLegacyEstimated=1
        UNION ALL SELECT Id,'Услуга','Старое питание: получатель не определён, нужна сверка' FROM ServiceOrders WHERE MealKind<>'None' AND GuestId IS NULL
        UNION ALL SELECT o.Id,'Услуга','Повторное питание за день; исторические суммы сохранены' FROM ServiceOrders o WHERE o.MealKind<>'None' AND o.GuestId IS NOT NULL AND
            EXISTS(SELECT 1 FROM ServiceOrders other WHERE other.Id<>o.Id AND other.GuestId=o.GuestId AND other.MealKind=o.MealKind AND other.OrderDate=o.OrderDate)
        UNION ALL SELECT o.Id,'Услуга','Превышен лимит старой услуги; нужна сверка' FROM ServiceOrders o WHERE o.RepeatRule<>'Repeatable' AND
            (o.Quantity<>1 OR (o.MealKind='None' AND EXISTS(SELECT 1 FROM ServiceOrders other WHERE other.Id<>o.Id AND other.StayId=o.StayId AND other.ServiceId=o.ServiceId AND (o.RepeatRule='Once' OR other.OrderDate=o.OrderDate))))
        UNION ALL SELECT b.Id,'Бронь','Период пересекается с другой бронью или проживанием; требуется сверка' FROM Bookings b
            WHERE b.Status IN('Новое','Подтверждено') AND b.DateTo>hotel_today() AND (
                EXISTS(SELECT 1 FROM Bookings other WHERE other.Id<>b.Id AND other.RoomId=b.RoomId AND other.Status IN('Новое','Подтверждено') AND b.DateFrom<other.DateTo AND b.DateTo>other.DateFrom)
                OR EXISTS(SELECT 1 FROM Stays s WHERE s.RoomId=b.RoomId AND s.Status='Проживает' AND COALESCE(s.BookingId,0)<>b.Id AND b.DateTo>s.CheckInDate AND (s.PlannedCheckOutDate<=hotel_today() OR b.DateFrom<s.PlannedCheckOutDate)))
        UNION ALL SELECT b.Id,'Бронь','Статус и карточка проживания не согласованы' FROM Bookings b WHERE
            (b.Status='Заселен' AND NOT EXISTS(SELECT 1 FROM Stays s WHERE s.BookingId=b.Id)) OR
            EXISTS(SELECT 1 FROM Stays s WHERE s.BookingId=b.Id AND (b.Status<>'Заселен' OR s.RoomId<>b.RoomId OR s.GuestId<>b.GuestId OR s.PlannedCheckOutDate<>b.DateTo))
        UNION ALL SELECT p.Id,'Оплата','Сумма не совпадает с деталями старого платежа' FROM Payments p WHERE p.AmountKopecks<>COALESCE((SELECT SUM(ps.AmountKopecks) FROM PaymentStays ps WHERE ps.PaymentId=p.Id),0)
        """);
}
