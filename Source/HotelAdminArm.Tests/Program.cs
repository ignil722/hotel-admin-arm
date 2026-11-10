using System.Data;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HotelAdminArm;
using HotelAdminArm.Data;
using HotelAdminArm.Domain;
using Microsoft.Data.Sqlite;

internal static class Program
{
    private static readonly DateTime Today=new(2026,10,8);
    private static readonly string Root=Path.Combine(Path.GetTempPath(),"HotelAdminArm-tests",Guid.NewGuid().ToString("N"));
    private static int passed,failed;
    private static int g1,g2,r1,r2,service;
    private static void Assert(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}catch(SqliteException){return;}throw new Exception("Invalid operation was accepted");}
    private static byte[] Photo(){using var b=new Bitmap(2,2);using var m=new MemoryStream();b.Save(m,System.Drawing.Imaging.ImageFormat.Png);return m.ToArray();}
    private static void Reset()
    {
        SqliteConnection.ClearAllPools();Database.Clock=()=>Today;
        Database.DatabasePath=Path.Combine(Root,$"case-{passed+failed}.db");Database.Initialize(false);
        Database.Execute("INSERT INTO RoomTypes(Name,Description) VALUES('Test','Test')");
        g1=HotelService.SaveGuest(0,"Тестов","Первый","",null,"AA-0001","","","",true);
        g2=HotelService.SaveGuest(0,"Тестов","Второй","",null,"AA-0002","","","",true);
        r1=HotelService.SaveRoom(0,"101",1,1,2,100,false,true,"custom notes",Photo());
        r2=HotelService.SaveRoom(0,"102",1,1,2,200,false,true,"",null);
        service=HotelService.SaveService(0,"Стирка одежды",10,true);
    }
    private static void Test(string name,Action action,bool reset=true)
    {
        try{if(reset)Reset();action();passed++;Console.WriteLine("PASS "+name);}
        catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
    }
    private static int Book(DateTime? from=null,DateTime? to=null,string status="Подтверждено",int room=0)=>HotelService.SaveBooking(0,g1,room==0?r1:room,from??Today,to??Today.AddDays(4),status,"");
    private static int Stay(int room=0,DateTime? to=null)=>HotelService.CheckIn(g1,room==0?r1:room,to??Today.AddDays(4),"");
    private static bool Free(int room,DateTime? from=null)=>HotelService.Available(from??Today,(from??Today).AddDays(1)).AsEnumerable().Any(r=>Convert.ToInt32(r["Id"])==room);
    private static void Close(int stay){var bill=HotelService.Preview(stay);HotelService.Checkout(new[]{stay},g1,"Наличные",bill.Fingerprint);}
    private static long Count(string table)=>Database.Scalar<long>("SELECT count(*) FROM "+table);

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        if(args.Contains("--meal-dates"))
        {
            MealDates();SqliteConnection.ClearAllPools();Console.WriteLine($"RESULT {passed} passed, {failed} failed. Artifacts: {Root}");return failed==0?0:1;
        }
        if(args.Contains("--layouts"))
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);Application.EnableVisualStyles();Reset();
            var index=Array.IndexOf(args,"--layouts");var folder=index+1<args.Length?Path.GetFullPath(args[index+1]):Root;
            if(args.Contains("--demo")){Database.DatabasePath=Path.Combine(Root,"layout-demo.db");Database.Initialize();}
            CaptureLayouts(folder);Console.WriteLine("LAYOUTS "+folder);return 0;
        }
        Test("01 booking visible in rooms",()=>{Book();Assert(Convert.ToString(HotelService.Rooms().Rows[0]["Сейчас"])=="Забронирован");Assert(!Free(r1));});
        Test("02 walk-in cannot consume booking",()=>{Book();Reject(()=>Stay());});
        Test("03 booking cannot consume active stay",()=>{Stay();Reject(()=>Book());});
        Test("04 duplicate booking rejected",()=>{Book();Reject(()=>Book());});
        Test("05 manual occupied status rejected",()=>Reject(()=>Database.Execute("UPDATE Rooms SET Status='Занят' WHERE Id=@id",Database.P("id",r1))));
        Test("06 overdue stay blocks future search",()=>{Database.Clock=()=>Today.AddDays(-8);HotelService.CheckIn(g1,r1,Today.AddDays(-1),"");Database.Clock=()=>Today;Assert(!Free(r1));Assert(!Free(r1,Today.AddDays(20)));});
        Test("07 second active stay blocked even disjoint",()=>{Stay(to:Today.AddDays(1));Database.Clock=()=>Today.AddDays(2);Reject(()=>Stay(to:Today.AddDays(4)));});
        Test("08 settled status cannot be forged",()=>{var b=Book();Reject(()=>Database.Execute("UPDATE Bookings SET Status='Заселен' WHERE Id=@id",Database.P("id",b)));});
        Test("09 settled booking identity protected",()=>{var b=Book();HotelService.CheckIn(0,0,Today,"",b);Reject(()=>HotelService.SaveBooking(b,g2,r2,Today,Today.AddDays(4),"Подтверждено",""));});
        Test("10 inhabited booking cannot cancel",()=>{var b=Book();HotelService.CheckIn(0,0,Today,"",b);Reject(()=>HotelService.CancelBooking(b));});
        Test("11 inhabited booking cannot delete",()=>{var b=Book();HotelService.CheckIn(0,0,Today,"",b);Reject(()=>HotelService.DeleteBooking(b));});
        Test("12 repair cannot cover active reservation",()=>{Book();Reject(()=>HotelService.SaveRoom(r1,"101",1,1,2,100,true,true,"",null));});
        Test("13 early check-in rejected",()=>{Book(to:Today.AddDays(2));var later=Book(Today.AddDays(3),Today.AddDays(6));Reject(()=>HotelService.CheckIn(0,0,Today,"",later));});
        Test("14 future actual arrival rejected",()=>Reject(()=>Database.Execute("INSERT INTO Stays(GuestId,RoomId,CheckInDate,PlannedCheckOutDate) VALUES(@g,@r,'2026-10-15','2026-10-18')",Database.P("g",g1),Database.P("r",r1))));
        Test("15 room and service historical prices frozen",()=>{var s=Stay();HotelService.AddOrder(s,service,2);Database.Clock=()=>Today.AddDays(2);Assert(HotelService.Preview(s).Total==22000);HotelService.SaveRoom(r1,"101",1,1,2,999,false,true,"",null);HotelService.SaveService(service,"Другое название",99,true);Assert(HotelService.Preview(s).Total==22000);});
        Test("16 departed stay cannot pay twice",()=>{var s=Stay();Close(s);var n=Count("Payments");Reject(()=>Close(s));Assert(Count("Payments")==n);});
        Test("17 closed service orders cannot delete",()=>{var s=Stay();HotelService.AddOrder(s,service,1);var order=Database.Scalar<int>("SELECT Id FROM ServiceOrders");Close(s);Reject(()=>HotelService.DeleteOrder(order));});
        Test("18 invalid dates rejected by database",()=>Reject(()=>Database.Execute("INSERT INTO Bookings(GuestId,RoomId,DateFrom,DateTo) VALUES(@g,@r,'bad','date')",Database.P("g",g1),Database.P("r",r1))));
        Test("19 inconsistent payment rejected",()=>Reject(()=>Database.Execute("INSERT INTO Payments(PayerGuestId,PaymentDate,Amount,AmountKopecks,Method) VALUES(@g,'2026-10-08',999,99900,'Наличные')",Database.P("g",g1))));
        Test("20 repair preserved after other checkout",()=>{HotelService.SaveRoom(r2,"102",1,1,2,200,true,true,"",null);Close(Stay());Assert(Database.Scalar<int>("SELECT UnderMaintenance FROM Rooms WHERE Id=@id",Database.P("id",r2))==1);});
        Test("21 empty rooms does not repeat seed",()=>{Database.DatabasePath=Path.Combine(Root,"empty.db");Database.Initialize(false);Database.Execute("INSERT INTO RoomTypes(Name,Description) VALUES('Only','')");Database.Initialize();Assert(Count("Rooms")==0 && Count("RoomTypes")==1);});
        Test("22 adjacent booking periods accepted",()=>{Book(to:Today.AddDays(2));Book(Today.AddDays(2),Today.AddDays(4));Assert(Count("Bookings")==2);});
        Test("23 overlapping active stays rejected",()=>{Stay();Reject(()=>Stay());});
        Test("24 zero tariff checkout with no fake payment",()=>{HotelService.SaveRoom(r1,"101",1,1,2,0,false,true,"",null);var s=Stay();Close(s);Assert(Count("Payments")==0 && Count("GuestArchive")==1);Assert(Database.Scalar<long>("SELECT SettlementKopecks FROM Stays WHERE Id=@id",Database.P("id",s))==0);});
        Test("full lifecycle and consolidated payment",()=>{
            var b=Book();var s1=HotelService.CheckIn(0,0,Today,"",b);var s2=Stay(r2);HotelService.AddOrder(s1,service,2);Database.Clock=()=>Today.AddDays(2);
            var quote=HotelService.Preview(s1,s2);Assert(quote.Total==62000);var p=HotelService.Checkout(new[]{s1,s2},g2,"Банковская карта",quote.Fingerprint);
            Assert(p>0 && Count("Payments")==1 && Count("PaymentStays")==2 && Count("GuestArchive")==2);Assert(Free(r1,Today.AddDays(2)));Assert(HotelService.ClosedBill(s1).Contains("Стирка одежды"));
        });
        Test("unconfirmed reservation cannot check in",()=>{var b=Book(status:"Новое");Reject(()=>HotelService.CheckIn(0,0,Today,"",b));});
        Test("extension checks next booking and syncs own booking",()=>{var b=Book(to:Today.AddDays(2));var s=HotelService.CheckIn(0,0,Today,"",b);Book(Today.AddDays(4),Today.AddDays(6));Reject(()=>HotelService.Extend(s,Today.AddDays(5)));HotelService.Extend(s,Today.AddDays(3));Assert(Database.Scalar<string>("SELECT DateTo FROM Bookings WHERE Id=@id",Database.P("id",b))=="2026-10-11");});
        Test("move preserves tariffs and does not double-charge day",()=>{var b=Book();var s=HotelService.CheckIn(0,0,Today,"",b);Database.Clock=()=>Today.AddDays(1);HotelService.Move(s,r2);Assert(Free(r1,Today.AddDays(1)));Assert(Database.Scalar<int>("SELECT RoomId FROM Bookings WHERE Id=@id",Database.P("id",b))==r2);Database.Clock=()=>Today.AddDays(2);Assert(HotelService.Preview(s).Total==30000);Close(s);Assert(Count("StaySegments")==2);});
        Test("same-day multiple moves minimum one night",()=>{var s=Stay();HotelService.Move(s,r2);HotelService.Move(s,r1);Assert(HotelService.Preview(s).Total==10000);});
        Test("occupants capacity and owner protection",()=>{var s=Stay();HotelService.AddOccupant(s,g2);var g3=HotelService.SaveGuest(0,"Третий","Гость","",null,"AA3","","","",true);Reject(()=>HotelService.AddOccupant(s,g3));Reject(()=>HotelService.RemoveOccupant(s,g1));HotelService.RemoveOccupant(s,g2);Assert(Count("StayGuests")==1);});
        Test("room capacity cannot shrink below occupants",()=>{var s=Stay();HotelService.AddOccupant(s,g2);Reject(()=>HotelService.SaveRoom(r1,"101",1,1,1,100,false,true,"",null));});
        Test("passport normalization and unknown birth",()=>{Reject(()=>HotelService.SaveGuest(0,"Дубль","Паспорт","",null,"aa 0001","","","",true));Assert(Database.Scalar<long>("SELECT count(*) FROM Guests WHERE BirthDate IS NULL")==2);Reject(()=>HotelService.SaveGuest(0,"X","Y","",Today.AddDays(1),"NEW","","","",true));});
        Test("bad contacts rejected",()=>{Reject(()=>HotelService.SaveGuest(0,"X","Y","",null,"NEW","abc","","",true));Reject(()=>HotelService.SaveGuest(0,"X","Y","",null,"NEW","","wrong email","",true));});
        Test("quote changes fail atomically",()=>{var s=Stay();var quote=HotelService.Preview(s);HotelService.AddOrder(s,service,1);Reject(()=>HotelService.Checkout(new[]{s},g1,"Наличные",quote.Fingerprint));Assert(Count("Payments")==0 && Count("InvoiceLines")==0 && Count("GuestArchive")==0);});
        Test("two connections revalidate reservation",()=>{var path=Database.DatabasePath;using var first=Database.OpenConnection();using var second=Database.OpenConnection();Book();Reject(()=>Book());Assert(Database.DatabasePath==path);});
        Test("late database failure rolls whole checkout back",()=>{
            var s1=Stay();var s2=Stay(r2);var quote=HotelService.Preview(s1,s2);
            Database.Execute($"CREATE TRIGGER test_failure BEFORE UPDATE ON Stays WHEN NEW.Id={s2} AND NEW.Status='Выбыл' BEGIN SELECT RAISE(ABORT,'test injected failure'); END;");
            Reject(()=>HotelService.Checkout(new[]{s1,s2},g1,"Наличные",quote.Fingerprint));Assert(Count("Payments")==0 && Count("PaymentStays")==0 && Count("InvoiceLines")==0 && Count("GuestArchive")==0);Assert(Database.Scalar<long>("SELECT count(*) FROM Stays WHERE Status='Проживает'")==2);
        });
        Test("backup and restore",()=>{var backup=Path.Combine(Root,"backup.db");Database.BackupTo(backup);Book();Database.RestoreFrom(backup);Assert(Count("Bookings")==0 && Count("Rooms")==2);Reject(()=>Database.RestoreFrom(Database.DatabasePath));SqliteConnection.ClearAllPools();File.WriteAllText(Database.DatabasePath,"damaged database");Database.RestoreFrom(backup);Assert(Count("Rooms")==2 && Count("Bookings")==0);});
        Test("demo data is coherent and repeat startup preserves photos",()=>{Database.DatabasePath=Path.Combine(Root,"demo.db");Database.Initialize();Assert(Count("Rooms")==20 && Count("Payments")==5);Assert(Database.Scalar<long>("SELECT count(*) FROM Stays WHERE Status='Выбыл' AND (SettlementKopecks IS NULL OR SettlementKopecks<>COALESCE((SELECT SUM(TotalKopecks) FROM InvoiceLines WHERE StayId=Stays.Id),0))")==0);var photo=Database.Query("SELECT Photo FROM Rooms WHERE Id=1").Rows[0][0] as byte[];Database.Initialize();Assert(photo!.SequenceEqual((byte[])Database.Query("SELECT Photo FROM Rooms WHERE Id=1").Rows[0][0]));});
        Test("migration real database copy preserves all records",MigrationReal,false);
        Test("UI selection and new-record reset",UI);
        Test("empty date calendar opens today and prevents past selection",DateCalendar);
        Test("expired booking dates stay out of editor and require selection",BookingDates);
        Test("operational dates follow day rollover and keep future selections",DateRollover);
        Test("concurrent conflicting reservations have one winner",()=>{
            var result=Task.WhenAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>{try{Book();return true;}catch(InvalidOperationException){return false;}catch(SqliteException){return false;}}))).GetAwaiter().GetResult();
            Assert(result.Count(x=>x)==1 && Count("Bookings")==1);
        });
        Test("closed invoice keeps original guest and payer names",()=>{
            var stay=Stay();Close(stay);var text=HotelService.ClosedBill(stay);
            HotelService.SaveGuest(g1,"Новое","Имя","",null,"AA-0001","","","",true);
            Assert(HotelService.ClosedBill(stay)==text && Database.Scalar<string>("SELECT PayerNameSnapshot FROM Payments").Contains("Тестов"));
        });
        Test("Cyrillic passport normalization",()=>{HotelService.SaveGuest(0,"X","Y","",null,"аб-123","","","",true);Reject(()=>HotelService.SaveGuest(0,"X","Y","",null,"АБ 123","","","",true));});
        Test("active supplemental guest cannot be disabled",()=>{var stay=Stay();HotelService.AddOccupant(stay,g2);Reject(()=>HotelService.SaveGuest(g2,"Тестов","Второй","",null,"AA-0002","","","",false));});
        Test("raw move requires segment history",()=>{var stay=Stay();Reject(()=>Database.Execute("UPDATE Stays SET RoomId=@room WHERE Id=@stay",Database.P("room",r2),Database.P("stay",stay)));});
        Test("closed order cannot be moved to active stay",()=>{var stay=Stay();HotelService.AddOrder(stay,service,1);Close(stay);var next=Stay(r2);Reject(()=>Database.Execute("UPDATE ServiceOrders SET StayId=@stay",Database.P("stay",next)));});
        Test("closed archive and tariff history protected",()=>{var stay=Stay();Close(stay);Reject(()=>Database.Execute("UPDATE GuestArchive SET FullName='Forged'"));Reject(()=>Database.Execute("UPDATE StaySegments SET RateKopecks=1"));Reject(()=>Database.Execute("DELETE FROM InvoiceLines"));});
        Test("repair and inactive rooms rejected at arrival",()=>{HotelService.SaveRoom(r1,"101",1,1,2,100,true,true,"",null);Reject(()=>Stay());Assert(!Free(r1));HotelService.SaveRoom(r2,"102",1,1,2,200,false,false,"",null);Reject(()=>Stay(r2));});
        Test("booking tariff persists when catalogue changes",()=>{var b=Book();HotelService.SaveRoom(r1,"101",1,1,2,999,false,true,"",null);var stay=HotelService.CheckIn(0,0,Today,"",b);Assert(HotelService.Preview(stay).Total==10000);});
        Test("early reservation can be rescheduled before check-in",()=>{var b=Book(Today.AddDays(2),Today.AddDays(4));HotelService.SaveBooking(b,g1,r1,Today,Today.AddDays(4),"Подтверждено","");Assert(HotelService.CheckIn(0,0,Today,"",b)>0);});
        Test("overdue stay can extend then checkout honestly",()=>{var stay=Stay(to:Today.AddDays(1));Database.Clock=()=>Today.AddDays(2);HotelService.Extend(stay,Today.AddDays(4));Close(stay);Assert(Database.Scalar<string>("SELECT ActualCheckOutDate FROM Stays WHERE Id=@id",Database.P("id",stay))=="2026-10-10");});
        Test("invalid backup preserves current database",()=>{Book();var invalid=Path.Combine(Root,"invalid.db");File.WriteAllText(invalid,"not sqlite");Reject(()=>Database.RestoreFrom(invalid));Assert(Count("Bookings")==1);});
        Test("migration duplicate passports rolls back",()=>{
            var legacy=CreateLegacy("duplicate-legacy.db");Database.DatabasePath=legacy;
            Database.Execute("UPDATE Guests SET Passport='11-11 111111' WHERE Id=2");Database.Execute("UPDATE Guests SET Passport='1111 111111' WHERE Id=1");
            Reject(()=>Database.Initialize(false));Assert(Database.Scalar<long>("PRAGMA user_version")==0 && Database.Scalar<long>("SELECT count(*) FROM pragma_table_info('Rooms') WHERE name='PriceKopecks'")==0 && Count("Guests")==24);
        });
        Test("invoice print preview pagination",()=>{
            using var document=MainForm.InvoiceDocument(string.Join("\n",Enumerable.Range(0,250).Select(i=>$"Строка счёта {i}: 100,00 руб.")));
            var controller=new System.Drawing.Printing.PreviewPrintController();document.PrintController=controller;document.Print();
            var pages=controller.GetPreviewPageInfo();Assert(pages.Length>1);foreach(var page in pages)page.Image.Dispose();
        });
        OrderRules();
        MealDates();
        Test("responsive sections preserve drafts selections and database",ResponsiveLayout);
        Test("version upgrade is idempotent",()=>{Task.WaitAll(Task.Run(()=>Database.Initialize(false)),Task.Run(()=>Database.Initialize(false)));var before=Count("Rooms");Database.Execute("PRAGMA user_version=3");Database.Initialize(false);Assert(Database.Scalar<long>("PRAGMA user_version")==Database.SchemaVersion && Count("Rooms")==before);Database.Initialize(false);});
        SqliteConnection.ClearAllPools();Console.WriteLine($"RESULT {passed} passed, {failed} failed. Artifacts: {Root}");return failed==0?0:1;
    }

    private static int Meal(string kind="Lunch",string name="Обед")=>HotelService.SaveService(0,name,10,true,kind,"Daily");
    private static void RawOrder(int stay,int svc,int guest,int qty=1,DateTime? date=null)=>Database.Execute("""
        INSERT INTO ServiceOrders(StayId,ServiceId,Quantity,OrderDate,UnitPriceKopecks,NameSnapshot,GuestId,GuestNameSnapshot,MealKind,RepeatRule)
        SELECT @stay,s.Id,@qty,@date,s.PriceKopecks,s.Name,g.Id,g.LastName||' '||g.FirstName||' '||IFNULL(g.MiddleName,''),s.MealKind,s.RepeatRule
        FROM Services s JOIN Guests g ON g.Id=@guest WHERE s.Id=@svc
        """,Database.P("stay",stay),Database.P("svc",svc),Database.P("guest",guest),Database.P("qty",qty),Database.P("date",date??Database.Today));
    private static void OrderRules()
    {
        Test("meal duplicate and rapid double submit rejected",()=>{var s=Stay();var m=Meal();HotelService.AddOrder(s,m,1,g1);Reject(()=>HotelService.AddOrder(s,m,1,g1));Assert(Count("ServiceOrders")==1);});
        Test("different occupants receive independent meals",()=>{var s=Stay();HotelService.AddOccupant(s,g2);var m=Meal();HotelService.AddOrder(s,m,1,g1);HotelService.AddOrder(s,m,1,g2);Assert(Count("ServiceOrders")==2);});
        Test("each meal kind and next day allowed",()=>{var s=Stay();var m=Meal();HotelService.AddOrder(s,m,1,g1);HotelService.AddOrder(s,Meal("Breakfast","Завтрак"),1,g1);HotelService.AddOrder(s,Meal("Dinner","Ужин"),1,g1);Database.Clock=()=>Today.AddDays(1);HotelService.AddOrder(s,m,1,g1);Assert(Count("ServiceOrders")==4);});
        Test("duplicate meal catalogue cannot bypass daily rule",()=>{var s=Stay();HotelService.AddOrder(s,Meal(),1,g1);Reject(()=>HotelService.AddOrder(s,Meal("Lunch","Бизнес-ланч"),1,g1));});
        Test("same guest another stay cannot repeat meal",()=>{var a=Stay();var b=Stay(r2);var m=Meal();HotelService.AddOrder(a,m,1,g1);Reject(()=>HotelService.AddOrder(b,m,1,g1));});
        Test("checkout and new arrival retain daily meal limit",()=>{var s=Stay();var m=Meal();HotelService.AddOrder(s,m,1,g1);Close(s);var next=Stay(r2);Reject(()=>HotelService.AddOrder(next,m,1,g1));});
        Test("meal requires actual occupant and one portion",()=>{var s=Stay();var m=Meal();Reject(()=>HotelService.AddOrder(s,m,1));Reject(()=>HotelService.AddOrder(s,m,1,g2));Reject(()=>HotelService.AddOrder(s,m,2,g1));Reject(()=>HotelService.AddOrder(s,m,0,g1));Reject(()=>HotelService.AddOrder(s,m,101,g1));Assert(Count("ServiceOrders")==0);});
        Test("raw database meal duplicate and invalid recipient rejected",()=>{var s=Stay();var m=Meal();Reject(()=>RawOrder(s,m,g2));Reject(()=>RawOrder(s,m,g1,2));RawOrder(s,m,g1);Reject(()=>RawOrder(s,m,g1));Assert(Count("ServiceOrders")==1);});
        Test("limited nonmeal rules across days and stays",()=>{var s=Stay();var other=Stay(r2);var daily=HotelService.SaveService(0,"Парковка",5,true,"None","Daily");var once=HotelService.SaveService(0,"Поздний выезд",5,true,"None","Once");HotelService.AddOrder(s,daily,1);Reject(()=>HotelService.AddOrder(s,daily,1));Reject(()=>HotelService.AddOrder(other,daily,2));HotelService.AddOrder(other,daily,1);HotelService.AddOrder(s,once,1);Database.Clock=()=>Today.AddDays(1);HotelService.AddOrder(s,daily,1);Reject(()=>HotelService.AddOrder(s,once,1));HotelService.AddOrder(other,once,1);});
        Test("repeatable quantities and invalid inputs",()=>{var s=Stay();HotelService.AddOrder(s,service,2);HotelService.AddOrder(s,service,100);Reject(()=>HotelService.AddOrder(s,service,0));Reject(()=>HotelService.AddOrder(s,service,101));Reject(()=>HotelService.AddOrder(s,service,1,g1));Assert(HotelService.Preview(s).Total==112000);});
        Test("closed stay inactive service and policy changes protected",()=>{var s=Stay();var m=Meal();HotelService.AddOrder(s,m,1,g1);Reject(()=>HotelService.SaveService(m,"Обед",10,true,"None","Repeatable"));HotelService.SaveService(m,"Обед новый",20,false,"Lunch","Daily");Reject(()=>HotelService.AddOrder(s,m,1,g1));Close(s);Reject(()=>HotelService.AddOrder(s,service,1));Reject(()=>HotelService.SaveService(0,"Bad",10,true,"Lunch","Repeatable"));});
        Test("raw rules snapshots date and quantity protected",()=>{var s=Stay();var m=Meal();RawOrder(s,m,g1);Reject(()=>Database.Execute("UPDATE ServiceOrders SET OrderDate='2026-10-07'"));Reject(()=>Database.Execute("UPDATE Services SET RepeatRule='Repeatable' WHERE Id=@id",Database.P("id",m)));Reject(()=>Database.Execute("INSERT INTO ServiceOrders(StayId,ServiceId,Quantity,OrderDate,UnitPriceKopecks,NameSnapshot) VALUES(@stay,@svc,1,hotel_today(),10,'Forged')",Database.P("stay",s),Database.P("svc",m)));});
        Test("concurrent meal insertion has one winner",()=>{var s=Stay();var m=Meal();var wins=0;Task.WaitAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>{try{HotelService.AddOrder(s,m,1,g1);Interlocked.Increment(ref wins);}catch(InvalidOperationException){}catch(SqliteException){}})).ToArray());Assert(wins==1 && Count("ServiceOrders")==1);});
        Test("meal recipient is frozen in closed invoice",()=>{var s=Stay();var m=Meal();HotelService.AddOrder(s,m,1,g1);Close(s);var text=HotelService.ClosedBill(s);HotelService.SaveGuest(g1,"Новая","Фамилия","",null,"AA-0001","","","",true);Assert(HotelService.ClosedBill(s)==text && text.Contains("Тестов Первый"));});
        Test("legacy meal migration preserves money and warns",()=>{
            var legacy=CreateLegacy("meal-legacy.db");Database.DatabasePath=legacy;
            Database.Execute("INSERT INTO ServiceOrders(StayId,ServiceId,Quantity,OrderDate) SELECT Id,2,2,CheckInDate FROM Stays LIMIT 1");
            var orders=Count("ServiceOrders");var paid=Database.Scalar<decimal>("SELECT sum(Amount) FROM Payments");Database.Initialize(false);
            Assert(Count("ServiceOrders")==orders && Database.Scalar<decimal>("SELECT sum(Amount) FROM Payments")==paid);
            Assert(Database.Scalar<long>("SELECT count(*) FROM Services WHERE MealKind<>'None' AND RepeatRule='Daily'")==3);
            Assert(HotelService.Warnings().AsEnumerable().Any(r=>Convert.ToString(r["Проблема"])!.Contains("получатель не определён")));
            Assert(Database.Query("PRAGMA foreign_key_check").Rows.Count==0 && Database.Scalar<string>("PRAGMA integrity_check")=="ok");
        });
        Test("service order editor recipient and quantity refresh",OrderEditor);
        Test("registration cards include every occupant",()=>{var s=Stay();HotelService.AddOccupant(s,g2);var card=HotelService.RegistrationCard(s);Assert(card.Contains("AA-0001") && card.Contains("AA-0002") && card.Contains("101") && card.Contains("2026-10-08"));Reject(()=>HotelService.RegistrationCard(0));});
        Test("meal names cannot be misclassified",()=>{Reject(()=>HotelService.SaveService(0," ОБЕД ",10,true));Reject(()=>Database.Execute("INSERT INTO Services(Name,Price,PriceKopecks,IsActive) VALUES('ЗАВТРАК',10,1000,1)"));Assert(HotelService.SuggestedMealKind(" Ужин ")=="Dinner");});
        Test("raw limited nonmeal duplicates and invalid quantity rejected",()=>{
            var s=Stay();var daily=HotelService.SaveService(0,"Парковка",10,true,"None","Daily");
            void Raw(int qty,string date)=>Database.Execute("INSERT INTO ServiceOrders(StayId,ServiceId,Quantity,OrderDate,UnitPriceKopecks,NameSnapshot,MealKind,RepeatRule) SELECT @stay,Id,@qty,@date,PriceKopecks,Name,MealKind,RepeatRule FROM Services WHERE Id=@id",Database.P("stay",s),Database.P("id",daily),Database.P("qty",qty),Database.P("date",date));
            Reject(()=>Raw(2,"2026-10-08"));Reject(()=>Raw(1,"2026-10-09"));Raw(1,"2026-10-08");Reject(()=>Raw(1,"2026-10-08"));Assert(Count("ServiceOrders")==1);
        });
        Test("version 4 migration preserves duplicate orders and backup",()=>{
            var s=Stay();HotelService.AddOrder(s,service,1);HotelService.AddOrder(s,service,2);
            foreach(DataRow trigger in Database.Query("SELECT name FROM sqlite_master WHERE type='trigger'").Rows)Database.Execute("DROP TRIGGER \""+Convert.ToString(trigger[0])!.Replace("\"","\"\"")+"\"");
            Database.Execute("DROP INDEX IX_Orders_MealGuestDate");
            foreach(var column in new[]{"GuestId","GuestNameSnapshot","MealKind","RepeatRule","IsLegacyRule"})Database.Execute("ALTER TABLE ServiceOrders DROP COLUMN "+column);
            Database.Execute("ALTER TABLE Services DROP COLUMN MealKind; ALTER TABLE Services DROP COLUMN RepeatRule; PRAGMA user_version=4;");
            Database.Execute("UPDATE Services SET Name='Обед'; UPDATE ServiceOrders SET NameSnapshot='Обед'");
            var before=HotelService.Kopecks(Database.Scalar<decimal>("SELECT sum(Quantity*UnitPriceKopecks)/100.0 FROM ServiceOrders"));
            Database.Initialize(false);
            Assert(Count("ServiceOrders")==2 && Database.Scalar<long>("SELECT sum(Quantity*UnitPriceKopecks) FROM ServiceOrders")==before);
            Assert(Directory.GetFiles(Path.Combine(Database.DataFolder,"Backups"),$"before-v{Database.SchemaVersion}-*.db").Length>0);
            Assert(HotelService.Warnings().AsEnumerable().Any(r=>Convert.ToString(r["Проблема"])!.Contains("получатель не определён")));
            Reject(()=>HotelService.AddOrder(s,service,1,g1));
            Database.Initialize(false);Assert(Database.Scalar<long>("PRAGMA user_version")==Database.SchemaVersion);
        });
    }

    private static void MealDates()
    {
        Test("meal selected date reaches order grid and invoice",()=>{
            var stay=Stay();var meal=Meal();var date=Today.AddDays(2);
            HotelService.AddOrder(stay,meal,1,g1,date);
            Assert(Database.Scalar<string>("SELECT OrderDate FROM ServiceOrders")==HotelService.Iso(date));
            Assert(HotelService.Preview(stay).Text.Contains(HotelService.Iso(date)));
            using var form=new MainForm();form.ErrorHandler=e=>throw e;form.Show();Application.DoEvents();
            var grid=(DataGridView)typeof(MainForm).GetField("_ordersGrid",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            Assert(((DataTable)grid.DataSource!).Rows[0]["Дата"].ToString()==HotelService.Iso(date));form.Close();
            Close(stay);Assert(HotelService.ClosedBill(stay).Contains(HotelService.Iso(date)));
        });
        Test("meal dates exclude past checkout and after checkout",()=>{
            var stay=Stay();var meal=Meal();
            foreach(var date in new[]{Today.AddDays(-1),Today.AddDays(4),Today.AddDays(5)})
            {Reject(()=>HotelService.AddOrder(stay,meal,1,g1,date));Reject(()=>RawOrder(stay,meal,g1,date:date));}
            HotelService.AddOrder(stay,meal,1,g1,Today.AddDays(3));Assert(Count("ServiceOrders")==1);
        });
        Test("same meal on different selected dates allowed and duplicate rejected",()=>{
            var stay=Stay();var meal=Meal();var date=Today.AddDays(1);
            HotelService.AddOrder(stay,meal,1,g1,date);Reject(()=>HotelService.AddOrder(stay,meal,1,g1,date));Reject(()=>RawOrder(stay,meal,g1,date:date));
            HotelService.AddOrder(stay,meal,1,g1,date.AddDays(1));Assert(Count("ServiceOrders")==2);
        });
        Test("all meal deadlines before at and after cutoff",()=>{
            var stay=Stay();
            foreach(var (kind,name,hour) in new[]{("Breakfast","Завтрак",10),("Lunch","Обед",14),("Dinner","Ужин",20)})
            {
                var meal=Meal(kind,name);Database.Clock=()=>Today.AddHours(hour).AddSeconds(-1);
                HotelService.AddOrder(stay,meal,1,g1,Today);Database.Execute("DELETE FROM ServiceOrders");
                foreach(var time in new[]{Today.AddHours(hour),Today.AddHours(hour).AddMinutes(1)})
                {Database.Clock=()=>time;Reject(()=>HotelService.AddOrder(stay,meal,1,g1,Today));Reject(()=>RawOrder(stay,meal,g1));}
                HotelService.AddOrder(stay,meal,1,g1,Today.AddDays(1));
            }
            Assert(Count("ServiceOrders")==1);
        });
        Test("ordinary services remain current date after meal cutoff",()=>{
            var stay=Stay();Database.Clock=()=>Today.AddHours(23);
            HotelService.AddOrder(stay,service,2,mealDate:Today.AddDays(2));
            Assert(Database.Scalar<string>("SELECT OrderDate FROM ServiceOrders")==HotelService.Iso(Today));
        });
        Test("overdue stay requires extension before meal scheduling",()=>{
            var stay=Stay(to:Today.AddDays(1));var meal=Meal();Database.Clock=()=>Today.AddDays(1);
            Reject(()=>HotelService.AddOrder(stay,meal,1,g1));HotelService.Extend(stay,Today.AddDays(3));
            HotelService.AddOrder(stay,meal,1,g1,Today.AddDays(2));
        });
        Test("future meal concurrent duplicate has one winner",()=>{
            var stay=Stay();var meal=Meal();var wins=0;
            Task.WaitAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>{try{HotelService.AddOrder(stay,meal,1,g1,Today.AddDays(2));Interlocked.Increment(ref wins);}catch(InvalidOperationException){}catch(SqliteException){}})).ToArray());
            Assert(wins==1 && Count("ServiceOrders")==1);
        });
        Test("version 5 upgrade preserves existing orders and creates backup",()=>{
            var stay=Stay();HotelService.AddOrder(stay,Meal(),1,g1);Close(stay);
            var text=HotelService.ClosedBill(stay);var total=Database.Scalar<long>("SELECT sum(AmountKopecks) FROM Payments");
            Database.Execute("PRAGMA user_version=5");Database.Clock=()=>Today.AddHours(23);Database.Initialize(false);
            Assert(Database.Scalar<long>("PRAGMA user_version")==6 && Count("ServiceOrders")==1 && HotelService.ClosedBill(stay)==text);
            Assert(Database.Scalar<long>("SELECT sum(AmountKopecks) FROM Payments")==total);
            Assert(Directory.GetFiles(Path.Combine(Database.DataFolder,"Backups"),"before-v6-*.db").Length>0);
            Assert(Database.Scalar<string>("PRAGMA integrity_check")=="ok" && Database.Query("PRAGMA foreign_key_check").Rows.Count==0);
            Database.Initialize(false);Assert(Count("ServiceOrders")==1);
        });
        Test("meal date editor updates bounds guest stay and cutoff",MealDateEditor);
    }

    private static void MealDateEditor()
    {
        var stay=Stay();HotelService.AddOccupant(stay,g2);var other=Stay(r2,to:Today.AddDays(2));var meal=Meal("Breakfast","Завтрак");
        using var form=new MainForm{StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000)};
        form.ErrorHandler=e=>throw e;form.Show();Application.DoEvents();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string n)=>(T)typeof(MainForm).GetField(n,flags)!.GetValue(form)!;
        Field<TabControl>("_tabs").SelectedIndex=5;
        Field<ComboBox>("_orderStayCombo").SelectedValue=stay;Field<ComboBox>("_orderServiceCombo").SelectedValue=meal;
        var date=Field<DateTimePicker>("_orderDatePicker");
        Assert(date.Enabled && date.MinDate==Today && date.MaxDate==Today.AddDays(3) && date.Value.Date==Today);
        var snapshot=Environment.GetEnvironmentVariable("HOTEL_MEAL_QA_PATH");
        if(!string.IsNullOrWhiteSpace(snapshot))
        {
            form.Size=new Size(1600,1000);Application.DoEvents();
            using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(snapshot);
        }
        date.Value=Today.AddDays(2);Field<ComboBox>("_orderGuestCombo").SelectedValue=g2;Assert(date.Value.Date==Today.AddDays(2));
        Field<ComboBox>("_orderStayCombo").SelectedValue=other;Assert(date.MaxDate==Today.AddDays(1) && date.Value.Date<=date.MaxDate);
        Database.Clock=()=>Today.AddHours(10);typeof(MainForm).GetMethod("UpdateOrderRule",flags)!.Invoke(form,null);
        Assert(date.MinDate==Today.AddDays(1) && date.Value.Date==Today.AddDays(1));
        Field<ComboBox>("_orderServiceCombo").SelectedValue=service;Assert(!date.Enabled);
        Database.Clock=()=>Today.AddDays(2);Field<ComboBox>("_orderServiceCombo").SelectedValue=meal;Assert(!date.Enabled);
        form.Close();
    }

    private static void OrderEditor()
    {
        var stay=Stay();HotelService.AddOccupant(stay,g2);var meal=Meal();
        using var form=new MainForm{StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000),WindowState=FormWindowState.Normal};
        var errors=new List<Exception>();form.ErrorHandler=errors.Add;form.Show();Application.DoEvents();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string n)=>(T)typeof(MainForm).GetField(n,flags)!.GetValue(form)!;
        Field<TabControl>("_tabs").SelectedIndex=5;
        Field<ComboBox>("_orderStayCombo").SelectedValue=stay;Field<ComboBox>("_orderServiceCombo").SelectedValue=meal;Application.DoEvents();
        var lookup=Field<ComboBox>("_orderServiceCombo");var source=lookup.DataSource;
        typeof(Form).GetMethod("OnActivated",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,new object[]{EventArgs.Empty});Application.DoEvents();
        Assert(ReferenceEquals(source,lookup.DataSource),"Activation rebound the open editor lookup");
        var guest=Field<ComboBox>("_orderGuestCombo");var qty=Field<NumericUpDown>("_orderQuantityNumeric");
        Assert(guest.Items.Count==2 && guest.Enabled && qty.Maximum==1 && !qty.Enabled);
        guest.SelectedValue=g1;typeof(MainForm).GetMethod("AddServiceOrder",flags)!.Invoke(form,null);
        Assert(Count("ServiceOrders")==1 && errors.Count==0);
        typeof(MainForm).GetMethod("AddServiceOrder",flags)!.Invoke(form,null);Assert(Count("ServiceOrders")==1 && errors.Count==1);errors.Clear();
        Field<ComboBox>("_orderServiceCombo").SelectedValue=service;Application.DoEvents();Assert(!guest.Enabled && qty.Maximum==100 && qty.Enabled);
        form.Close();
    }

    private static void CaptureLayouts(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach(var scale in new[]{1f,1.25f,1.5f})
        foreach(var size in new[]{new Size(1024,768),new Size(1366,768),new Size(1920,1080)})
        {
            var errors=new List<Exception>();
            using var form=new MainForm{ErrorHandler=errors.Add,WindowState=FormWindowState.Normal,StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000)};
            form.Show();Application.DoEvents();
            // Exercise both control and font scaling without changing Windows settings.
            IEnumerable<Control> Descendants(Control c)=>new[]{c}.Concat(c.Controls.Cast<Control>().SelectMany(Descendants));
            var originals=Descendants(form).ToDictionary(c=>c,c=>c.Font);
            var fonts=new List<Font>();
            form.Scale(new SizeF(scale,scale));
            foreach(var pair in originals){var font=new Font(pair.Value.FontFamily,pair.Value.Size*scale,pair.Value.Style,pair.Value.Unit);fonts.Add(font);pair.Key.Font=font;}
            form.MinimumSize=Size.Empty;form.Size=size;form.PerformLayout();Application.DoEvents();
            Console.WriteLine($"LAYOUT {size.Width} {scale}: form={form.Size}, client={form.ClientSize}, root={form.Controls[0].Size}");
            var tabs=(TabControl)typeof(MainForm).GetField("_tabs",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            for(var i=0;i<tabs.TabCount;i++)
            {
                tabs.SelectedIndex=i;Application.DoEvents();
                var inner=tabs.TabPages[i].Controls.Cast<Control>().SelectMany(Descendants).OfType<TabControl>().FirstOrDefault();
                for(var section=0;section<(inner?.TabCount??1);section++)
                {
                if(inner is not null){inner.SelectedIndex=section;Application.DoEvents();}
                using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
                bitmap.Save(Path.Combine(folder,$"{size.Width}-{scale*100:000}-{i+1:00}-{section+1:00}.png"));
                foreach(var button in Descendants(tabs.TabPages[i]).OfType<Button>().Where(b=>b.Visible))
                {
                    var ancestors=new List<Control>();for(Control? p=button.Parent;p is not null;p=p.Parent)ancestors.Add(p);
                    foreach(var scroll in ancestors.AsEnumerable().Reverse().OfType<ScrollableControl>().Where(c=>c.AutoScroll))scroll.ScrollControlIntoView(button);
                    Application.DoEvents();
                    var visible=button.RectangleToScreen(button.ClientRectangle);
                    foreach(var ancestor in ancestors)visible=Rectangle.Intersect(visible,ancestor.RectangleToScreen(ancestor.ClientRectangle));
                    Assert(visible.Width>=button.Width-1 && visible.Height>=button.Height-1,$"Clipped button: {size.Width}/{scale}/{tabs.TabPages[i].Text}/{button.Text}: {visible.Size} vs {button.Size}");
                }
                }
            }
            Assert(errors.Count==0,string.Join("\n",errors));form.Close();foreach(var font in fonts)font.Dispose();
        }
    }

    private static void ResponsiveLayout()
    {
        var stay=Stay();var booking=Book(Today.AddDays(1),Today.AddDays(4),room:r2);
        using var form=new MainForm{WindowState=FormWindowState.Normal,StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000)};
        var errors=new List<Exception>();form.ErrorHandler=errors.Add;form.Show();Application.DoEvents();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(MainForm).GetField(name,flags)!.GetValue(form)!;
        var tabs=Field<TabControl>("_tabs");tabs.SelectedIndex=1;
        var rooms=Field<DataGridView>("_roomsGrid");rooms.CurrentCell=rooms.Rows[0].Cells["Номер"];Application.DoEvents();
        var source=rooms.DataSource;var number=Field<TextBox>("_roomNumberText");
        var search=Field<Dictionary<DataGridView,TextBox>>("_searchBoxes")[rooms];search.Text="101";number.Text="Draft-999";
        var count=Database.Scalar<long>("SELECT count(*) FROM AuditLog");
        IEnumerable<Control> All(Control c)=>new[]{c}.Concat(c.Controls.Cast<Control>().SelectMany(All));
        foreach(var size in new[]{new Size(1920,1080),new Size(1024,768),new Size(1366,768),new Size(1920,1080)})
        {
            form.Size=size;Application.DoEvents();
            Assert(number.Text=="Draft-999" && search.Text=="101" && ReferenceEquals(source,rooms.DataSource),"Resize reset the draft or data source");
            var split=All(tabs.TabPages[1]).OfType<ResponsiveWorkspace>().Single();
            Assert(split.Orientation==(size.Width==1024?Orientation.Horizontal:Orientation.Vertical),$"Wrong responsive orientation: requested={size}, actual={form.Size}, workspace={split.ClientSize}, DPI={split.DeviceDpi}, font={split.Font.Size}");
        }
        tabs.SelectedIndex=3;var sections=Field<TabControl>("_bookingSections");
        var comment=Field<TextBox>("_bookingCommentText");comment.Text="Unsaved booking";sections.SelectedIndex=1;sections.SelectedIndex=0;Application.DoEvents();
        Assert(comment.Text=="Unsaved booking","Switching sections reset the draft");
        tabs.SelectedIndex=4;Assert(Field<TabControl>("_staySections").TabCount==3);
        tabs.SelectedIndex=5;Assert(Field<TabControl>("_serviceSections").TabCount==2);
        tabs.SelectedIndex=8;Application.DoEvents();Assert(!Field<DataGridView>("_warningsGrid").Visible && !Field<DataGridView>("_todayGrid").Visible,"Empty grids did not show their empty state");
        Assert(Database.Scalar<long>("SELECT count(*) FROM AuditLog")==count && errors.Count==0,"Layout modified data or raised an error");
        form.Close();
    }

    private static string CreateLegacy(string name)
    {
        var path=Path.Combine(Root,name);Database.DatabasePath=path;
        using var connection=Database.OpenConnection();
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        var sql=(string)typeof(Database).GetField("SchemaSql",flags)!.GetRawConstantValue()!;
        using(var command=connection.CreateCommand()){command.CommandText=sql;command.ExecuteNonQuery();}
        typeof(Database).GetMethod("Seed",flags)!.Invoke(null,new object[]{connection});return path;
    }
    private static void MigrationReal()
    {
        var original=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HotelAdminArm","hotel.db");
        if(!File.Exists(original))original=CreateLegacy("legacy-fixture.db");
        byte[] HashOriginal(){using var stream=new FileStream(original,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return SHA256.HashData(stream);}
        var before=HashOriginal();
        var copy=Path.Combine(Root,"real-copy.db");
        using(var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=original,Mode=SqliteOpenMode.ReadOnly}.ToString()))
        using(var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=copy}.ToString())){source.Open();target.Open();source.BackupDatabase(target);}
        Database.DatabasePath=copy;Database.Clock=()=>Today;
        var roomSnapshot=Database.Query("SELECT Id,Number,Notes,Photo FROM Rooms ORDER BY Id");var paymentSnapshot=Database.Query("SELECT Id,Amount FROM Payments ORDER BY Id");
        var counts=new[]{"Rooms","Guests","Bookings","Stays","Services","ServiceOrders","Payments","PaymentStays","GuestArchive"}.ToDictionary(t=>t,Count);
        Database.Initialize(false);foreach(var pair in counts)Assert(Count(pair.Key)==pair.Value,"Migration changed "+pair.Key);
        var rooms=Database.Query("SELECT Id,Number,Notes,Photo FROM Rooms ORDER BY Id");
        for(var i=0;i<rooms.Rows.Count;i++)for(var j=0;j<4;j++)Assert(rooms.Rows[i][j] is byte[] blob?blob.SequenceEqual((byte[])roomSnapshot.Rows[i][j]):Equals(rooms.Rows[i][j],roomSnapshot.Rows[i][j]),"Room data changed");
        var payments=Database.Query("SELECT Id,Amount FROM Payments ORDER BY Id");for(var i=0;i<payments.Rows.Count;i++)Assert(Equals(payments.Rows[i][1],paymentSnapshot.Rows[i][1]),"Historical payment changed");
        Assert(HotelService.Available(Today,Today.AddDays(1)).Rows.Count<Count("Rooms"),"Overdue rooms still available");
        Assert(Database.Query("PRAGMA foreign_key_check").Rows.Count==0 && Database.Scalar<string>("PRAGMA integrity_check")=="ok");
        var logs=Count("AuditLog");Database.Initialize(false);Assert(Count("AuditLog")==logs,"Migration repeated");
        Assert(before.SequenceEqual(HashOriginal()),"Original database was modified");
    }
    [DllImport("user32.dll", CharSet=CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);

    private static void DateCalendar()
    {
        Database.Clock=()=>new DateTime(2026,10,9);
        using var form=new Form{StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000)};
        using var picker=new OperationalDatePicker(allowEmpty:true){Width=180};
        form.Controls.Add(picker);form.Show();Application.DoEvents();
        Assert(picker.SelectedDate is null && picker.Text.Trim()=="","Date was filled before selection");
        Reject(()=>picker.RequireDate("заезд"));
        picker.Value=Database.Today.AddMonths(2);picker.SelectedDate=null;
        Assert(picker.Text.Trim()=="","New booking retains a date");
        // Open the actual Windows calendar using the drop-down button messages.
        var point=new IntPtr((10<<16)|(picker.Width-12));
        SendMessage(picker.Handle,0x201,new IntPtr(1),point);
        SendMessage(picker.Handle,0x202,IntPtr.Zero,point);Application.DoEvents();
        var calendar=SendMessage(picker.Handle,0x1008,IntPtr.Zero,IntPtr.Zero);
        Assert(calendar!=IntPtr.Zero,"Native calendar did not open");
        var buffer=Marshal.AllocHGlobal(32);
        try
        {
            Assert(SendMessage(calendar,0x1001,IntPtr.Zero,buffer)!=IntPtr.Zero,"Calendar selection unavailable");
            DateTime ReadDate(int offset)=>new(Marshal.ReadInt16(buffer,offset),Marshal.ReadInt16(buffer,offset+2),Marshal.ReadInt16(buffer,offset+6));
            Assert(ReadDate(0)==Database.Today,"Calendar opened on another month/day/year");
            var range=SendMessage(calendar,0x1011,IntPtr.Zero,buffer).ToInt64();
            Assert((range&1)==1 && ReadDate(0)==Database.Today,"Windows calendar permits past dates");
        }
        finally { Marshal.FreeHGlobal(buffer); }
        SendMessage(picker.Handle,0x100D,IntPtr.Zero,IntPtr.Zero);Application.DoEvents();
        Assert(picker.RequireDate("заезд")==Database.Today,"Today cannot be selected from an empty calendar");
        try { picker.Value=Database.Today.AddDays(-1);throw new Exception("Past date accepted"); }
        catch(ArgumentOutOfRangeException) { }
        Assert(picker.SelectedDate==Database.Today,"Past assignment changed the selected date");
        form.Close();
    }

    private static void BookingDates()
    {
        Database.Clock=()=>Today.AddDays(-20);
        var historical=Book(Today.AddDays(-20),Today.AddDays(-18));
        Database.Clock=()=>Today;
        var future=Book(Today.AddDays(5),Today.AddDays(8));
        var snapshot=Database.Scalar<string>("SELECT DateFrom||'/'||DateTo FROM Bookings WHERE Id=@id",Database.P("id",historical));
        var errors=new List<Exception>();
        using var form=new MainForm{ErrorHandler=errors.Add,WindowState=FormWindowState.Normal,StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000)};
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(MainForm).GetField(name,flags)!.GetValue(form)!;
        void Invoke(string name,params object[] args)=>typeof(MainForm).GetMethod(name,flags)!.Invoke(form,args);
        form.Show();Field<TabControl>("_tabs").SelectedIndex=3;Application.DoEvents();
        var from=Field<OperationalDatePicker>("_bookingFromPicker");var to=Field<OperationalDatePicker>("_bookingToPicker");
        Assert(from.SelectedDate is null && to.SelectedDate is null,"Initial editor contains automatic dates");
        var grid=Field<DataGridView>("_bookingsGrid");
        void Select(int id){grid.Focus();grid.CurrentCell=grid.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToInt32(r.Cells["Id"].Value)==id).Cells["Заезд"];Application.DoEvents();}
        Select(historical);
        Assert(from.Text.Trim()=="" && to.Text.Trim()=="","Expired booking populated past dates");
        Invoke("SaveBooking",true);
        Assert(errors.Count==1 && errors[0].Message.Contains("Выберите дату"),"Blank editor did not explain missing date");errors.Clear();
        Assert(snapshot==Database.Scalar<string>("SELECT DateFrom||'/'||DateTo FROM Bookings WHERE Id=@id",Database.P("id",historical)),"Historical dates changed");
        Select(future);Assert(from.SelectedDate==Today.AddDays(5) && to.SelectedDate==Today.AddDays(8),"Future booking lost valid dates");
        Invoke("ClearBookingForm");Assert(from.Text.Trim()=="" && to.Text.Trim()=="","New booking is not empty");
        Field<ComboBox>("_bookingGuestCombo").SelectedValue=g2;Field<ComboBox>("_bookingRoomCombo").SelectedValue=r2;
        from.Value=Today;Invoke("SaveBooking",false);
        Assert(errors.Count==1 && errors[0].Message.Contains("выезд") && Count("Bookings")==2,"Unselected departure created a booking");errors.Clear();
        to.Value=Today.AddDays(2);Invoke("SaveBooking",false);
        Assert(Count("Bookings")==3 && from.SelectedDate==Today,"Explicitly selected dates cannot create a booking");
        Assert(errors.Count==0,string.Join("\n",errors));
        using var bitmap=new Bitmap(form.Width,form.Height);Invoke("ClearBookingForm");form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(Root,"booking-empty-dates.png"));
        form.Close();
    }

    private static void DateRollover()
    {
        var errors=new List<Exception>();
        using var form=new MainForm{ErrorHandler=errors.Add,WindowState=FormWindowState.Normal,StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000)};
        form.Show();Application.DoEvents();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        OperationalDatePicker Field(string name)=>(OperationalDatePicker)typeof(MainForm).GetField(name,flags)!.GetValue(form)!;
        Field("_bookingFromPicker").Value=Today;Field("_bookingToPicker").Value=Today.AddDays(5);
        Database.Clock=()=>Today.AddDays(1);
        typeof(MainForm).GetMethod("RefreshAll",flags)!.Invoke(form,new object[]{false});Application.DoEvents();
        Assert(Field("_bookingFromPicker").SelectedDate is null,"Yesterday remained selected after midnight");
        Assert(Field("_bookingToPicker").SelectedDate==Today.AddDays(5),"Day rollover erased a future choice");
        foreach(var name in new[]{"_bookingFromPicker","_bookingToPicker","_availableFromPicker","_calendarFrom"})
            Assert(Field(name).MinDate==Database.Today,"Date bounds did not track current day: "+name);
        Assert(Field("_availableToPicker").Value>=Database.Today.AddDays(1) && Field("_stayPlannedOutPicker").Value>=Database.Today.AddDays(1),"Departure default expired");
        var birth=(DateTimePicker)typeof(MainForm).GetField("_guestBirthDatePicker",flags)!.GetValue(form)!;
        birth.Value=new DateTime(1980,1,1);Assert(birth.Value.Year==1980,"Birthday was limited to future dates");
        Database.Clock=()=>new DateTime(2027,1,1);
        typeof(MainForm).GetMethod("RefreshAll",flags)!.Invoke(form,new object[]{false});
        Assert(Field("_bookingToPicker").SelectedDate is null && Field("_calendarFrom").MinDate==Database.Today,"Year change retained expired dates");
        Assert(errors.Count==0,string.Join("\n",errors));form.Close();
    }

    private static void UI()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);Application.EnableVisualStyles();
        var errors=new List<Exception>();
        Application.ThreadException+=(_,e)=>errors.Add(e.Exception);
        using var form=new MainForm{ErrorHandler=e=>{Console.WriteLine("UI handled: "+e);errors.Add(e);},WindowState=FormWindowState.Normal,StartPosition=FormStartPosition.Manual,Location=new Point(-10000,-10000),Size=new Size(1200,800)};
        form.Show();Application.DoEvents();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        ((TabControl)typeof(MainForm).GetField("_tabs",flags)!.GetValue(form)!).SelectedIndex=1;Application.DoEvents();
        Assert(((ComboBox)typeof(MainForm).GetField("_stayGuestCombo",flags)!.GetValue(form)!).SelectedIndex==-1,"Walk-in guest silently selected");
        var grid=(DataGridView)typeof(MainForm).GetField("_roomsGrid",flags)!.GetValue(form)!;
        var number=(TextBox)typeof(MainForm).GetField("_roomNumberText",flags)!.GetValue(form)!;
        grid.CurrentCell=grid.Rows[1].Cells["Номер"];grid.Rows[1].Selected=true;Application.DoEvents();Assert(number.Text=="102",$"Selected editor={number.Text}, id={grid.CurrentRow?.Cells["Id"].Value}");
        typeof(MainForm).GetMethod("RefreshAll",flags)!.Invoke(form,new object[]{true});Application.DoEvents();Assert(number.Text=="102" && grid.CurrentRow is not null && Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value)==r2,$"Editor={number.Text}; current={grid.CurrentRow?.Cells["Id"].Value}");
        var searches=(Dictionary<DataGridView,TextBox>)typeof(MainForm).GetField("_searchBoxes",flags)!.GetValue(form)!;
        searches[grid].Text="no-such-room";Application.DoEvents();Assert((int)typeof(MainForm).GetField("_roomEditId",flags)!.GetValue(form)! == 0 && number.Text=="");
        searches[grid].Text="";Application.DoEvents();
        typeof(MainForm).GetMethod("ClearRoomForm",flags)!.Invoke(form,null);Assert(grid.CurrentRow is null && number.Text=="" && (int)typeof(MainForm).GetField("_roomEditId",flags)!.GetValue(form)! == 0);
        foreach(var size in new[]{new Size(1000,700),new Size(1600,1000)}){form.Size=size;Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(Root,$"ui-{size.Width}.png"));}
        var tabs=(TabControl)typeof(MainForm).GetField("_tabs",flags)!.GetValue(form)!;
        for(var i=0;i<tabs.TabCount;i++){tabs.SelectedIndex=i;Application.DoEvents();}
        var calendar=(DataGridView)typeof(MainForm).GetField("_calendarGrid",flags)!.GetValue(form)!;
        Assert(calendar.Rows.Count==2 && calendar.Columns.Count==32 && Convert.ToString(calendar.Rows[0].Cells[2].Value)=="Свободен");
        Book();typeof(MainForm).GetMethod("RefreshAll",flags)!.Invoke(form,new object[]{true});tabs.SelectedIndex=7;Application.DoEvents();
        Assert(Convert.ToString(calendar.Rows[0].Cells[2].Value)=="Бронь");
        form.Close();Assert(errors.Count==0,string.Join("\n",errors));
    }
}
