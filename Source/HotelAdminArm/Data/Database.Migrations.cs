using System.Data;
using Microsoft.Data.Sqlite;
using HotelAdminArm.Domain;

namespace HotelAdminArm.Data;

internal static partial class Database
{
    // Версия 6 меняет только правила дат питания; сохранённые заказы и суммы не пересчитываются.
    internal const int SchemaVersion = 6;

    private static void UpgradeSchema(SqliteConnection c, bool fresh)
    {
        var version = Scalar<long>(c, null, "PRAGMA user_version;");
        if (version > SchemaVersion) throw new InvalidOperationException("База создана более новой версией программы.");
        if (version == SchemaVersion) return;
        // Обновление существующей базы начинается с SQLite-копии до транзакционной замены правил.
        if (!fresh) BackupTo(Path.Combine(DataFolder, "Backups", $"before-v{SchemaVersion}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db"));
        using var tx = c.BeginTransaction();
        try
        {
            version=Scalar<long>(c,tx,"PRAGMA user_version");
            if(version==SchemaVersion){tx.Commit();return;}
            var triggers = Read(c, tx, "SELECT name FROM sqlite_master WHERE type='trigger'");
            foreach (DataRow row in triggers.Rows) Execute(c, tx, $"DROP TRIGGER \"{Convert.ToString(row[0])!.Replace("\"", "\"\"")}\";");
            if (version < 1)
            {
                foreach (var column in new[] {
                    "Rooms ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN(0,1))",
                    "Rooms ADD COLUMN UnderMaintenance INTEGER NOT NULL DEFAULT 0 CHECK(UnderMaintenance IN(0,1))",
                    "Rooms ADD COLUMN PriceKopecks INTEGER NOT NULL DEFAULT 0 CHECK(PriceKopecks>=0 AND typeof(PriceKopecks)='integer')",
                    "Guests ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN(0,1))",
                    "Guests ADD COLUMN PassportKey TEXT NOT NULL DEFAULT ''",
                    "Bookings ADD COLUMN RateKopecks INTEGER NOT NULL DEFAULT 0 CHECK(RateKopecks>=0 AND typeof(RateKopecks)='integer')",
                    "Bookings ADD COLUMN IsLegacyEstimated INTEGER NOT NULL DEFAULT 0",
                    "Stays ADD COLUMN RateKopecks INTEGER NOT NULL DEFAULT 0 CHECK(RateKopecks>=0 AND typeof(RateKopecks)='integer')",
                    "Stays ADD COLUMN IsLegacyEstimated INTEGER NOT NULL DEFAULT 0",
                    "Stays ADD COLUMN SettlementKopecks INTEGER CHECK(SettlementKopecks IS NULL OR (SettlementKopecks>=0 AND typeof(SettlementKopecks)='integer'))",
                    "Services ADD COLUMN PriceKopecks INTEGER NOT NULL DEFAULT 0 CHECK(PriceKopecks>=0 AND typeof(PriceKopecks)='integer')",
                    "ServiceOrders ADD COLUMN UnitPriceKopecks INTEGER NOT NULL DEFAULT 0 CHECK(UnitPriceKopecks>=0 AND typeof(UnitPriceKopecks)='integer')",
                    "ServiceOrders ADD COLUMN NameSnapshot TEXT NOT NULL DEFAULT ''",
                    "ServiceOrders ADD COLUMN IsLegacyEstimated INTEGER NOT NULL DEFAULT 0",
                    "Payments ADD COLUMN AmountKopecks INTEGER NOT NULL DEFAULT 0 CHECK(AmountKopecks>=0 AND typeof(AmountKopecks)='integer')",
                    "Payments ADD COLUMN Finalized INTEGER NOT NULL DEFAULT 1 CHECK(Finalized IN(0,1))",
                    "PaymentStays ADD COLUMN AmountKopecks INTEGER NOT NULL DEFAULT 0 CHECK(AmountKopecks>=0 AND typeof(AmountKopecks)='integer')" })
                    Execute(c, tx, "ALTER TABLE " + column);
                Execute(c, tx, """
                    UPDATE Rooms SET PriceKopecks=CAST(round(DailyPrice*100) AS INTEGER), UnderMaintenance=(Status='Ремонт');
                    UPDATE Services SET PriceKopecks=CAST(round(Price*100) AS INTEGER);
                    UPDATE Bookings SET RateKopecks=(SELECT PriceKopecks FROM Rooms WHERE Id=RoomId), IsLegacyEstimated=@legacy;
                    UPDATE Stays SET RateKopecks=(SELECT PriceKopecks FROM Rooms WHERE Id=RoomId), IsLegacyEstimated=@legacy;
                    UPDATE ServiceOrders SET UnitPriceKopecks=(SELECT PriceKopecks FROM Services WHERE Id=ServiceId),
                        NameSnapshot=(SELECT Name FROM Services WHERE Id=ServiceId), IsLegacyEstimated=@legacy;
                    UPDATE Payments SET AmountKopecks=CAST(round(Amount*100) AS INTEGER);
                    UPDATE PaymentStays SET AmountKopecks=CAST(round(Amount*100) AS INTEGER);
                    UPDATE Stays SET SettlementKopecks=(SELECT SUM(AmountKopecks) FROM PaymentStays WHERE StayId=Stays.Id)
                        WHERE Status='Выбыл' AND EXISTS(SELECT 1 FROM PaymentStays WHERE StayId=Stays.Id);
                    """, P("legacy", fresh ? 0 : 1));
                var guests = Read(c, tx, "SELECT Id,Passport FROM Guests");
                foreach (DataRow guest in guests.Rows)
                    Execute(c, tx, "UPDATE Guests SET PassportKey=@key WHERE Id=@id", P("key", HotelService.NormalizePassport(Convert.ToString(guest["Passport"])!)), P("id", guest["Id"]));
            }
            if (version < 3)
            {
                Execute(c,tx,"ALTER TABLE Payments ADD COLUMN PayerNameSnapshot TEXT NOT NULL DEFAULT '';");
                Execute(c,tx,"UPDATE Payments SET PayerNameSnapshot=(SELECT LastName||' '||FirstName||' '||IFNULL(MiddleName,'') FROM Guests WHERE Id=PayerGuestId);");
            }
            var conflicts=new List<string>();
            foreach(DataRow row in Read(c,tx,"SELECT PassportKey,group_concat(Id) AS Ids FROM Guests GROUP BY PassportKey HAVING count(*)>1").Rows)
                conflicts.Add($"Повторный паспорт {row["PassportKey"]}; гости ID: {row["Ids"]}");
            foreach(DataRow row in Read(c,tx,"SELECT StayId,group_concat(PaymentId) AS Ids FROM PaymentStays GROUP BY StayId HAVING count(*)>1").Rows)
                conflicts.Add($"Несколько оплат проживания ID {row["StayId"]}; платежи ID: {row["Ids"]}");
            foreach(DataRow row in Read(c,tx,"SELECT RoomId,group_concat(Id) AS Ids FROM Stays WHERE Status='Проживает' GROUP BY RoomId HAVING count(*)>1").Rows)
                conflicts.Add($"Несколько активных проживаний в комнате ID {row["RoomId"]}; проживания ID: {row["Ids"]}");
            if(conflicts.Count>0) {
                var report=Path.Combine(DataFolder,"Backups",$"migration-conflicts-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllLines(report,conflicts);
                throw new InvalidOperationException("Миграция отменена. Записи не объединялись; оригинал и резервная копия сохранены.\n"+string.Join("\n",conflicts)+"\nОтчёт: "+report);
            }
            Execute(c, tx, """
                CREATE TABLE IF NOT EXISTS StaySegments(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, StayId INTEGER NOT NULL REFERENCES Stays(Id) ON DELETE RESTRICT,
                    RoomId INTEGER NOT NULL REFERENCES Rooms(Id) ON DELETE RESTRICT, RoomNumber TEXT NOT NULL,
                    DateFrom TEXT NOT NULL, DateTo TEXT,
                    RateKopecks INTEGER NOT NULL CHECK(RateKopecks>=0 AND typeof(RateKopecks)='integer'));
                CREATE TABLE IF NOT EXISTS StayGuests(
                    StayId INTEGER NOT NULL REFERENCES Stays(Id) ON DELETE RESTRICT,
                    GuestId INTEGER NOT NULL REFERENCES Guests(Id) ON DELETE RESTRICT, PRIMARY KEY(StayId,GuestId));
                CREATE TABLE IF NOT EXISTS InvoiceLines(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, StayId INTEGER NOT NULL REFERENCES Stays(Id) ON DELETE RESTRICT,
                    Description TEXT NOT NULL, Quantity INTEGER NOT NULL CHECK(Quantity>0),
                    UnitKopecks INTEGER NOT NULL CHECK(UnitKopecks>=0 AND typeof(UnitKopecks)='integer'),
                    TotalKopecks INTEGER NOT NULL CHECK(TotalKopecks=Quantity*UnitKopecks));
                INSERT INTO StaySegments(StayId,RoomId,RoomNumber,DateFrom,DateTo,RateKopecks)
                    SELECT s.Id,s.RoomId,r.Number,s.CheckInDate,s.ActualCheckOutDate,s.RateKopecks FROM Stays s JOIN Rooms r ON r.Id=s.RoomId
                    WHERE NOT EXISTS(SELECT 1 FROM StaySegments WHERE StayId=s.Id);
                INSERT OR IGNORE INTO StayGuests SELECT Id,GuestId FROM Stays;
                CREATE UNIQUE INDEX IF NOT EXISTS UX_Guests_PassportKey ON Guests(PassportKey);
                CREATE UNIQUE INDEX IF NOT EXISTS UX_PaymentStays_Stay ON PaymentStays(StayId);
                CREATE UNIQUE INDEX IF NOT EXISTS UX_StaySegments_Open ON StaySegments(StayId) WHERE DateTo IS NULL;
                CREATE INDEX IF NOT EXISTS IX_Bookings_RoomPeriod ON Bookings(RoomId,Status,DateFrom,DateTo);
                CREATE INDEX IF NOT EXISTS IX_Stays_RoomStatus ON Stays(RoomId,Status);
                CREATE INDEX IF NOT EXISTS IX_Orders_Stay ON ServiceOrders(StayId);
                """);
            if(fresh) Execute(c,tx,"""
                INSERT INTO InvoiceLines(StayId,Description,Quantity,UnitKopecks,TotalKopecks)
                SELECT s.Id,'Демонстрационный счёт: номер '||r.Number,
                    max(1,CAST(julianday(s.ActualCheckOutDate)-julianday(s.CheckInDate) AS INTEGER)),s.RateKopecks,s.SettlementKopecks
                FROM Stays s JOIN Rooms r ON r.Id=s.RoomId WHERE s.Status='Выбыл';
                """);
            var duplicates = Scalar<long>(c, tx, "SELECT COUNT(*) FROM (SELECT RoomId FROM Stays WHERE Status='Проживает' GROUP BY RoomId HAVING count(*)>1)");
            if (duplicates > 0) throw new InvalidOperationException("В старой базе несколько активных проживаний в одном номере. Исправьте конфликт в копии базы; оригинал и резервная копия сохранены.");
            Execute(c,tx,"CREATE UNIQUE INDEX IF NOT EXISTS UX_Stays_ActiveRoom ON Stays(RoomId) WHERE Status='Проживает';");
            if(version<5) UpgradeOrderRules(c,tx);
            InstallGuards(c, tx);
            InstallOrderRules(c,tx);
            Execute(c, tx, "UPDATE Rooms SET Status=CASE WHEN UnderMaintenance=1 THEN 'Ремонт' ELSE 'Свободен' END;");
            Execute(c, tx, $"PRAGMA user_version={SchemaVersion};");
            Execute(c, tx, "INSERT OR REPLACE INTO AppSettings(Key,Value) VALUES('HotelName',@name),('DesignVersion',@design);", P("name", HotelBranding.HotelName), P("design", HotelBranding.DesignVersion));
            if (Read(c, tx, "PRAGMA foreign_key_check").Rows.Count != 0) throw new InvalidOperationException("Нарушены связи старой базы. Миграция отменена.");
            tx.Commit();
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            tx.Rollback();
            throw new InvalidOperationException("Миграция отменена: обнаружены конфликтующие записи (в том числе повторные паспорта или оплаты). Оригинал сохранён. " + e.Message, e);
        }
    }

    internal static DataTable Read(SqliteConnection c, SqliteTransaction? tx, string sql, params SqliteParameter[] p)
    {
        using var cmd = BuildCommand(c, tx, sql, p);
        using var reader = cmd.ExecuteReader();
        // Sqlite's schema metadata treats partial unique indexes as global uniqueness.
        // Materialize values without copying those misleading constraints to a DataTable.
        var table = new DataTable();
        for(var i=0;i<reader.FieldCount;i++) table.Columns.Add(reader.GetName(i), reader.HasRows?reader.GetFieldType(i):typeof(object));
        while(reader.Read()){var values=new object[reader.FieldCount];reader.GetValues(values);table.Rows.Add(values);}
        return table;
    }

    public static void BackupTo(string path)
    {
        if (Path.GetFullPath(path).Equals(Path.GetFullPath(DatabasePath), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Выберите другой файл для резервной копии.");
        if (File.Exists(path)) throw new InvalidOperationException("Файл уже существует. Выберите новое имя резервной копии.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var source = OpenConnection();
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path }.ToString());
        target.Open(); source.BackupDatabase(target);
    }

    public static void RestoreFrom(string path)
    {
        if (Path.GetFullPath(path).Equals(Path.GetFullPath(DatabasePath), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Выберите резервную копию, а не рабочую базу.");
        var staged = Path.Combine(DataFolder, $"restore-{Guid.NewGuid():N}.db");
        var original = DatabasePath;
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Mode=SqliteOpenMode.ReadOnly }.ToString()))
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=staged }.ToString()))
            {
                source.Open(); target.Open(); source.BackupDatabase(target);
            }
            DatabasePath = staged;
            using (var check = OpenConnection())
                if (Scalar<string>(check,null,"PRAGMA integrity_check") != "ok" || Scalar<long>(check,null,"SELECT count(*) FROM sqlite_master WHERE name='Rooms'") == 0)
                    throw new InvalidOperationException("Файл не является исправной базой гостиницы.");
            Initialize(false);
            if (Query("PRAGMA foreign_key_check").Rows.Count > 0) throw new InvalidOperationException("Резервная копия содержит нарушенные связи.");
            DatabasePath = original;
            SqliteConnection.ClearAllPools();
            var corrupt=false;
            if (File.Exists(original))
            {
                var preserved=Path.Combine(DataFolder,"Backups",$"before-restore-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db");
                try { BackupTo(preserved); }
                catch(SqliteException e) when(e.SqliteErrorCode is 11 or 26)
                {
                    // A damaged file cannot be copied through SQLite. Preserve its raw
                    // bytes and journal files before replacing it with the validated copy.
                    SqliteConnection.ClearAllPools();
                    preserved=Path.Combine(DataFolder,"Backups",$"damaged-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.db");
                    Directory.CreateDirectory(Path.GetDirectoryName(preserved)!);
                    File.Copy(original,preserved,false);corrupt=true;
                    foreach(var suffix in new[]{"-wal","-shm","-journal"})
                        if(File.Exists(original+suffix))File.Move(original+suffix,preserved+suffix);
                }
            }
            if(corrupt)
            {
                if(!Path.GetDirectoryName(Path.GetFullPath(staged))!.Equals(Path.GetDirectoryName(Path.GetFullPath(original)),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Временная база должна находиться рядом с рабочей.");
                File.Move(staged,original,true);return;
            }
            using var restored = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=staged }.ToString());
            restored.Open(); using var destination = OpenConnection(); restored.BackupDatabase(destination);
        }
        finally { DatabasePath=original; SqliteConnection.ClearAllPools(); if(File.Exists(staged)) File.Delete(staged); }
    }

    private static void InstallGuards(SqliteConnection c, SqliteTransaction tx)
    {
        void Guard(string name,string table,string operation,string conditions) => Execute(c,tx,$"CREATE TRIGGER {name} BEFORE {operation} ON {table} BEGIN {conditions} END;");
        string Reject(string condition,string message) => $"SELECT CASE WHEN {condition} THEN RAISE(ABORT,'{message}') END;";
        string Dates(string from,string to) => Reject($"length({from})<>10 OR length({to})<>10 OR date({from},'+0 days') IS NULL OR date({to},'+0 days') IS NULL OR date({from},'+0 days')<>{from} OR date({to},'+0 days')<>{to} OR {to}<={from}","Некорректный период проживания");
        var overlapBookings = "EXISTS(SELECT 1 FROM Bookings b WHERE b.RoomId=NEW.RoomId AND b.Id<>NEW.Id AND b.Status IN('Новое','Подтверждено') AND NEW.DateFrom<b.DateTo AND NEW.DateTo>b.DateFrom)";
        var overlapStays = "EXISTS(SELECT 1 FROM Stays s WHERE s.RoomId=NEW.RoomId AND s.Status='Проживает' AND NEW.DateTo>s.CheckInDate AND (s.PlannedCheckOutDate<=hotel_today() OR NEW.DateFrom<s.PlannedCheckOutDate))";
        var bookingRule=Dates("NEW.DateFrom","NEW.DateTo")+
            Reject("NEW.Status IN('Новое','Подтверждено') AND NOT EXISTS(SELECT 1 FROM Guests WHERE Id=NEW.GuestId AND IsActive=1)","Выберите активного гостя")+
            Reject("NEW.Status IN('Новое','Подтверждено') AND EXISTS(SELECT 1 FROM Rooms WHERE Id=NEW.RoomId AND (UnderMaintenance=1 OR IsActive=0))","Номер недоступен")+
            Reject("NEW.Status IN('Новое','Подтверждено') AND ("+overlapBookings+" OR "+overlapStays+")","Номер занят или забронирован на выбранный период")+
            Reject("NEW.Status='Заселен' AND NOT EXISTS(SELECT 1 FROM Stays s WHERE s.BookingId=NEW.Id AND s.GuestId=NEW.GuestId AND s.RoomId=NEW.RoomId AND s.PlannedCheckOutDate=NEW.DateTo)","Заселение выполняется только через карточку проживания");
        Guard("booking_insert","Bookings","INSERT",bookingRule+Reject("NEW.DateFrom<hotel_today() OR NEW.Status NOT IN('Новое','Подтверждено')","Создайте новую или подтверждённую бронь сегодняшней или будущей датой")+
            Reject("NEW.RateKopecks<>(SELECT PriceKopecks FROM Rooms WHERE Id=NEW.RoomId)","Сохраните тариф выбранного номера"));
        Guard("booking_update","Bookings","UPDATE",bookingRule+
            Reject("NEW.RoomId=OLD.RoomId AND NEW.RateKopecks<>OLD.RateKopecks","Согласованный тариф брони защищён")+
            Reject("OLD.Status IN('Отменено','Заселен') AND NEW.Status<>OLD.Status","Нельзя изменить завершённую бронь")+
            Reject("OLD.Status='Заселен' AND (NEW.GuestId<>OLD.GuestId OR NEW.RateKopecks<>OLD.RateKopecks OR NEW.DateFrom<>OLD.DateFrom)","История заселённой брони защищена"));
        Guard("booking_delete","Bookings","DELETE",Reject("OLD.Status<>'Отменено' OR EXISTS(SELECT 1 FROM Stays WHERE BookingId=OLD.Id)","Сначала отмените бронь; история заселения не удаляется"));
        var stayRule=Dates("NEW.CheckInDate","NEW.PlannedCheckOutDate")+
            Reject("NEW.ActualCheckOutDate IS NOT NULL AND (date(NEW.ActualCheckOutDate,'+0 days') IS NULL OR date(NEW.ActualCheckOutDate,'+0 days')<>NEW.ActualCheckOutDate OR NEW.ActualCheckOutDate<NEW.CheckInDate)","Некорректная фактическая дата выезда")+
            Reject("NEW.Status='Проживает' AND (NEW.ActualCheckOutDate IS NOT NULL OR EXISTS(SELECT 1 FROM Rooms WHERE Id=NEW.RoomId AND (UnderMaintenance=1 OR IsActive=0)))","Номер недоступен для заселения")+
            Reject("NEW.Status='Проживает' AND EXISTS(SELECT 1 FROM Stays s WHERE s.RoomId=NEW.RoomId AND s.Status='Проживает' AND s.Id<>NEW.Id)","В номере уже проживает гость")+
            Reject("NEW.Status='Проживает' AND EXISTS(SELECT 1 FROM Bookings b WHERE b.RoomId=NEW.RoomId AND b.Status IN('Новое','Подтверждено') AND b.Id<>COALESCE(NEW.BookingId,0) AND hotel_today()<b.DateTo AND NEW.PlannedCheckOutDate>b.DateFrom)","Период пересекается с другой бронью")+
            Reject("NEW.Status='Выбыл' AND (NEW.ActualCheckOutDate IS NULL OR NEW.SettlementKopecks IS NULL)","Сначала оформите итоговый расчёт");
        Guard("stay_insert","Stays","INSERT",stayRule+
            Reject("NEW.RateKopecks<>CASE WHEN NEW.BookingId IS NULL THEN (SELECT PriceKopecks FROM Rooms WHERE Id=NEW.RoomId) ELSE (SELECT RateKopecks FROM Bookings WHERE Id=NEW.BookingId) END","Сохраните согласованный тариф заселения")+
            Reject("NOT EXISTS(SELECT 1 FROM Guests WHERE Id=NEW.GuestId AND IsActive=1)","Выберите активного гостя")+
            Reject("NEW.Status<>'Проживает' OR NEW.CheckInDate<>hotel_today()","Новый заезд оформляется текущей датой")+
            Reject("NEW.BookingId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM Bookings b WHERE b.Id=NEW.BookingId AND b.Status='Подтверждено' AND b.GuestId=NEW.GuestId AND b.RoomId=NEW.RoomId AND b.DateFrom<=hotel_today() AND b.DateTo=NEW.PlannedCheckOutDate)","Бронь не подтверждена или её даты не подходят"));
        Guard("stay_update","Stays","UPDATE",stayRule+
            Reject("NEW.RoomId<>OLD.RoomId AND NOT EXISTS(SELECT 1 FROM StaySegments WHERE StayId=NEW.Id AND RoomId=NEW.RoomId AND DateTo IS NULL AND DateFrom=hotel_today())","Переселение требует сохранённой истории номера")+
            Reject("OLD.Status='Выбыл'","Завершённое проживание защищено")+
            Reject("NEW.GuestId<>OLD.GuestId OR NEW.BookingId IS NOT OLD.BookingId OR NEW.CheckInDate<>OLD.CheckInDate OR NEW.RateKopecks<>OLD.RateKopecks","История проживания защищена")+
            Reject("NEW.Status='Проживает' AND NEW.SettlementKopecks IS NOT NULL","Расчёт сохраняется при выезде")+
            Reject("NEW.Status='Выбыл' AND NOT EXISTS(SELECT 1 FROM InvoiceLines WHERE StayId=NEW.Id)","Отсутствует итоговый счёт")+
            Reject("NEW.Status='Выбыл' AND NEW.ActualCheckOutDate<>hotel_today()","Фактический выезд оформляется сегодня")+
            Reject("NEW.Status='Выбыл' AND (NEW.SettlementKopecks<>COALESCE((SELECT SUM(TotalKopecks) FROM InvoiceLines WHERE StayId=NEW.Id),0) OR (NEW.SettlementKopecks>0 AND NOT EXISTS(SELECT 1 FROM PaymentStays ps JOIN Payments p ON p.Id=ps.PaymentId WHERE ps.StayId=NEW.Id AND p.Finalized=1 AND ps.AmountKopecks=NEW.SettlementKopecks)))","Итоговый счёт и оплата не совпадают"));
        Guard("stay_delete","Stays","DELETE",Reject("1","История проживания не удаляется"));
        Execute(c,tx,"""
            CREATE TRIGGER stay_open AFTER INSERT ON Stays BEGIN
                INSERT INTO StaySegments(StayId,RoomId,RoomNumber,DateFrom,RateKopecks)
                    SELECT NEW.Id,NEW.RoomId,Number,NEW.CheckInDate,NEW.RateKopecks FROM Rooms WHERE Id=NEW.RoomId;
                INSERT INTO StayGuests VALUES(NEW.Id,NEW.GuestId);
                UPDATE Bookings SET Status='Заселен' WHERE Id=NEW.BookingId;
            END;
            CREATE TRIGGER stay_sync AFTER UPDATE ON Stays BEGIN
                UPDATE Bookings SET RoomId=NEW.RoomId,DateTo=NEW.PlannedCheckOutDate WHERE Id=NEW.BookingId;
                INSERT INTO GuestArchive(StayId,GuestId,FullName,RoomNumber,CheckInDate,CheckOutDate,CreatedAt)
                    SELECT NEW.Id,g.Id,g.LastName||' '||g.FirstName||' '||IFNULL(g.MiddleName,''),r.Number,NEW.CheckInDate,NEW.ActualCheckOutDate,datetime('now','localtime')
                    FROM Guests g JOIN Rooms r ON r.Id=NEW.RoomId WHERE g.Id=NEW.GuestId AND NEW.Status='Выбыл';
            END;
            """);
        foreach(var op in new[]{"INSERT","UPDATE"})
        {
            Guard("room_"+op.ToLower(),"Rooms",op,Reject("NEW.Status<>CASE WHEN NEW.UnderMaintenance=1 THEN 'Ремонт' ELSE 'Свободен' END OR NEW.Capacity<1 OR NEW.Floor<1 OR trim(NEW.Number)=''","Проверьте номер, вместимость и состояние")+
                Reject("(NEW.UnderMaintenance=1 OR NEW.IsActive=0) AND (EXISTS(SELECT 1 FROM Stays WHERE RoomId=NEW.Id AND Status='Проживает') OR EXISTS(SELECT 1 FROM Bookings WHERE RoomId=NEW.Id AND Status IN('Новое','Подтверждено') AND DateTo>hotel_today()))","Сначала переселите гостей и перенесите или отмените будущие брони")+
                Reject("NEW.Capacity<(SELECT count(*) FROM StayGuests sg JOIN Stays s ON s.Id=sg.StayId WHERE s.RoomId=NEW.Id AND s.Status='Проживает')","Вместимость меньше числа проживающих"));
            Guard("guest_"+op.ToLower(),"Guests",op,Reject("trim(NEW.LastName)='' OR trim(NEW.FirstName)='' OR NEW.PassportKey='' OR NEW.PassportKey<>hotel_passport(NEW.Passport)","Проверьте ФИО и паспорт")+
                Reject("NEW.IsActive=0 AND (EXISTS(SELECT 1 FROM StayGuests sg JOIN Stays s ON s.Id=sg.StayId WHERE sg.GuestId=NEW.Id AND s.Status='Проживает') OR EXISTS(SELECT 1 FROM Bookings WHERE GuestId=NEW.Id AND Status IN('Новое','Подтверждено') AND DateTo>hotel_today()))","У гостя есть проживание или будущая бронь")+
                Reject("NEW.BirthDate IS NOT NULL AND (date(NEW.BirthDate,'+0 days') IS NULL OR date(NEW.BirthDate,'+0 days')<>NEW.BirthDate OR NEW.BirthDate>hotel_today())","Некорректная дата рождения"));
            Guard("orders_"+op.ToLower(),"ServiceOrders",op,Reject("NOT EXISTS(SELECT 1 FROM Stays WHERE Id=NEW.StayId AND Status='Проживает') OR NOT EXISTS(SELECT 1 FROM Services WHERE Id=NEW.ServiceId AND IsActive=1)","Услуга или проживание неактивны")+
                Reject("typeof(NEW.Quantity)<>'integer'","Количество услуги должно быть целым")+
                Reject("date(NEW.OrderDate,'+0 days') IS NULL OR date(NEW.OrderDate,'+0 days')<>NEW.OrderDate OR (NEW.MealKind='None' AND NEW.OrderDate<>hotel_today()) OR NEW.OrderDate<(SELECT CheckInDate FROM Stays WHERE Id=NEW.StayId)","Некорректная дата услуги"));
        }
        Guard("orders_delete","ServiceOrders","DELETE",Reject("NOT EXISTS(SELECT 1 FROM Stays WHERE Id=OLD.StayId AND Status='Проживает')","Услуги закрытого счёта защищены"));
        Guard("orders_history","ServiceOrders","UPDATE",Reject("OLD.StayId<>NEW.StayId OR OLD.ServiceId<>NEW.ServiceId OR OLD.UnitPriceKopecks<>NEW.UnitPriceKopecks OR OLD.NameSnapshot<>NEW.NameSnapshot OR NOT EXISTS(SELECT 1 FROM Stays WHERE Id=OLD.StayId AND Status='Проживает')","История заказанной услуги защищена"));
        Guard("occupant_insert","StayGuests","INSERT",Reject("NOT EXISTS(SELECT 1 FROM Stays WHERE Id=NEW.StayId AND Status='Проживает') OR NOT EXISTS(SELECT 1 FROM Guests WHERE Id=NEW.GuestId AND IsActive=1)","Проживание или гость неактивны")+
            Reject("(SELECT count(*) FROM StayGuests WHERE StayId=NEW.StayId)>=(SELECT r.Capacity FROM Stays s JOIN Rooms r ON r.Id=s.RoomId WHERE s.Id=NEW.StayId)","Превышена вместимость номера"));
        Guard("occupant_delete","StayGuests","DELETE",Reject("NOT EXISTS(SELECT 1 FROM Stays WHERE Id=OLD.StayId AND Status='Проживает' AND GuestId<>OLD.GuestId)","Нельзя удалить основного гостя или изменить архив"));
        Guard("occupant_update","StayGuests","UPDATE",Reject("1","Используйте добавление и удаление проживающего"));
        Guard("payment_insert","Payments","INSERT",Reject("NEW.Finalized<>0 OR NEW.AmountKopecks<>0 OR NEW.Amount<>0 OR trim(NEW.PayerNameSnapshot)='' OR NEW.PaymentDate<>hotel_today() OR NEW.Method NOT IN('Наличные','Банковская карта','Перевод')","Создайте черновик оплаты текущей датой"));
        Guard("payment_update","Payments","UPDATE",Reject("OLD.Finalized=1","Проведённая оплата защищена")+
            Reject("NEW.PayerGuestId<>OLD.PayerGuestId OR NEW.PayerNameSnapshot<>OLD.PayerNameSnapshot OR NEW.PaymentDate<>OLD.PaymentDate OR NEW.Method<>OLD.Method","Данные подтверждаемого платежа защищены")+
            Reject("NEW.Finalized=1 AND (NEW.AmountKopecks<=0 OR NEW.AmountKopecks<>COALESCE((SELECT SUM(AmountKopecks) FROM PaymentStays WHERE PaymentId=NEW.Id),0) OR abs(NEW.Amount*100-NEW.AmountKopecks)>0.001)","Сумма оплаты не совпадает с составом счёта"));
        Guard("payment_delete","Payments","DELETE",Reject("OLD.Finalized=1","Проведённая оплата не удаляется"));
        Guard("payment_detail_insert","PaymentStays","INSERT",Reject("NOT EXISTS(SELECT 1 FROM Payments WHERE Id=NEW.PaymentId AND Finalized=0) OR NOT EXISTS(SELECT 1 FROM Stays WHERE Id=NEW.StayId AND Status='Проживает') OR NEW.AmountKopecks<>COALESCE((SELECT SUM(TotalKopecks) FROM InvoiceLines WHERE StayId=NEW.StayId),0) OR abs(NEW.Amount*100-NEW.AmountKopecks)>0.001","Некорректный состав оплаты"));
        foreach(var op in new[]{"UPDATE","DELETE"})
        {
            Guard("payment_detail_"+op.ToLower(),"PaymentStays",op,Reject("1","Состав оплаты защищён"));
            Guard("invoice_"+op.ToLower(),"InvoiceLines",op,Reject("EXISTS(SELECT 1 FROM Stays WHERE Id=OLD.StayId AND Status='Выбыл')","Закрытый счёт защищён"));
            Guard("segment_"+op.ToLower(),"StaySegments",op,Reject("EXISTS(SELECT 1 FROM Stays WHERE Id=OLD.StayId AND Status='Выбыл')","История закрытого проживания защищена"));
        }
        Guard("invoice_insert","InvoiceLines","INSERT",Reject("typeof(NEW.Quantity)<>'integer' OR typeof(NEW.TotalKopecks)<>'integer' OR NOT EXISTS(SELECT 1 FROM Stays WHERE Id=NEW.StayId AND Status='Проживает')","Некорректный или закрытый счёт"));
        Guard("segment_insert","StaySegments","INSERT",Reject("NOT EXISTS(SELECT 1 FROM Stays WHERE Id=NEW.StayId AND Status='Проживает') OR NEW.DateTo IS NOT NULL OR NEW.DateFrom<>hotel_today()","Некорректный период переселения"));
        Guard("segment_history","StaySegments","UPDATE",Reject("NEW.StayId<>OLD.StayId OR NEW.RoomId<>OLD.RoomId OR NEW.RoomNumber<>OLD.RoomNumber OR NEW.DateFrom<>OLD.DateFrom OR NEW.RateKopecks<>OLD.RateKopecks OR OLD.DateTo IS NOT NULL OR NEW.DateTo IS NULL OR NEW.DateTo<>hotel_today() OR NEW.DateTo<NEW.DateFrom","Период и тариф проживания защищены"));
        Guard("segment_remove","StaySegments","DELETE",Reject("1","История номера не удаляется"));
        foreach(var op in new[]{"UPDATE","DELETE"})Guard("archive_"+op.ToLower(),"GuestArchive",op,Reject("1","Архив выезда защищён"));
        foreach(var table in new[]{"Rooms","Guests","Bookings","Stays","Services","ServiceOrders","Payments","StayGuests","StaySegments"})
            foreach(var op in new[]{"INSERT","UPDATE","DELETE"})
            {
                var prefix=op=="DELETE"?"OLD":"NEW";
                var id=table=="StayGuests"?$"{prefix}.StayId":$"{prefix}.Id";
                var details=op=="UPDATE" && table=="Bookings"?"'Статус: '||OLD.Status||' → '||NEW.Status":
                    op=="UPDATE"&&table=="Rooms"?"'Номер '||OLD.Number||' → '||NEW.Number||'; тариф '||OLD.PriceKopecks||' → '||NEW.PriceKopecks":
                    op=="UPDATE"&&table=="Stays"?"'Номер '||OLD.RoomId||' → '||NEW.RoomId||'; выезд '||OLD.PlannedCheckOutDate||' → '||NEW.PlannedCheckOutDate||'; '||NEW.Status":$"'{op}: {table}'";
                Execute(c,tx,$"CREATE TRIGGER audit_{table}_{op} AFTER {op} ON {table} BEGIN INSERT INTO AuditLog(Action,Entity,EntityId,Details) VALUES('{op}','{table}',{id},{details}); END;");
            }
    }
}
