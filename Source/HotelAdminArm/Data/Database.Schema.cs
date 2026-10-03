namespace HotelAdminArm.Data;

internal static partial class Database
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS RoomTypes (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Description TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Rooms (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Number TEXT NOT NULL UNIQUE,
            RoomTypeId INTEGER NOT NULL,
            Floor INTEGER NOT NULL,
            Capacity INTEGER NOT NULL,
            DailyPrice REAL NOT NULL CHECK (DailyPrice >= 0),
            Status TEXT NOT NULL DEFAULT 'Свободен' CHECK (Status IN ('Свободен', 'Занят', 'Ремонт')),
            Notes TEXT,
            Photo BLOB,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
            FOREIGN KEY (RoomTypeId) REFERENCES RoomTypes(Id)
        );

        CREATE TABLE IF NOT EXISTS Guests (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            LastName TEXT NOT NULL,
            FirstName TEXT NOT NULL,
            MiddleName TEXT,
            BirthDate TEXT,
            Passport TEXT NOT NULL UNIQUE,
            Phone TEXT,
            Email TEXT,
            Address TEXT,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
        );

        CREATE TABLE IF NOT EXISTS Bookings (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            GuestId INTEGER NOT NULL,
            RoomId INTEGER NOT NULL,
            DateFrom TEXT NOT NULL,
            DateTo TEXT NOT NULL,
            Status TEXT NOT NULL DEFAULT 'Новое' CHECK (Status IN ('Новое', 'Подтверждено', 'Заселен', 'Отменено')),
            Comment TEXT,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
            FOREIGN KEY (GuestId) REFERENCES Guests(Id) ON DELETE RESTRICT,
            FOREIGN KEY (RoomId) REFERENCES Rooms(Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS Stays (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            GuestId INTEGER NOT NULL,
            RoomId INTEGER NOT NULL,
            BookingId INTEGER,
            CheckInDate TEXT NOT NULL,
            PlannedCheckOutDate TEXT NOT NULL,
            ActualCheckOutDate TEXT,
            Status TEXT NOT NULL DEFAULT 'Проживает' CHECK (Status IN ('Проживает', 'Выбыл')),
            Notes TEXT,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
            FOREIGN KEY (GuestId) REFERENCES Guests(Id) ON DELETE RESTRICT,
            FOREIGN KEY (RoomId) REFERENCES Rooms(Id) ON DELETE RESTRICT,
            FOREIGN KEY (BookingId) REFERENCES Bookings(Id) ON DELETE SET NULL
        );

        CREATE TABLE IF NOT EXISTS Services (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            Price REAL NOT NULL CHECK (Price >= 0),
            IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1))
        );

        CREATE TABLE IF NOT EXISTS ServiceOrders (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StayId INTEGER NOT NULL,
            ServiceId INTEGER NOT NULL,
            Quantity INTEGER NOT NULL CHECK (Quantity > 0),
            OrderDate TEXT NOT NULL,
            FOREIGN KEY (StayId) REFERENCES Stays(Id) ON DELETE CASCADE,
            FOREIGN KEY (ServiceId) REFERENCES Services(Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS Payments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PayerGuestId INTEGER NOT NULL,
            PaymentDate TEXT NOT NULL,
            Amount REAL NOT NULL CHECK (Amount >= 0),
            Method TEXT NOT NULL,
            Comment TEXT,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
            FOREIGN KEY (PayerGuestId) REFERENCES Guests(Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS PaymentStays (
            PaymentId INTEGER NOT NULL,
            StayId INTEGER NOT NULL,
            Amount REAL NOT NULL CHECK (Amount >= 0),
            PRIMARY KEY (PaymentId, StayId),
            FOREIGN KEY (PaymentId) REFERENCES Payments(Id) ON DELETE CASCADE,
            FOREIGN KEY (StayId) REFERENCES Stays(Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS GuestArchive (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StayId INTEGER NOT NULL UNIQUE,
            GuestId INTEGER NOT NULL,
            FullName TEXT NOT NULL,
            RoomNumber TEXT NOT NULL,
            CheckInDate TEXT NOT NULL,
            CheckOutDate TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            FOREIGN KEY (StayId) REFERENCES Stays(Id) ON DELETE RESTRICT,
            FOREIGN KEY (GuestId) REFERENCES Guests(Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS AuditLog (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Action TEXT NOT NULL,
            Entity TEXT NOT NULL,
            EntityId INTEGER,
            Details TEXT,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
        );

        CREATE TABLE IF NOT EXISTS AppSettings (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS UX_Stays_BookingId
        ON Stays(BookingId)
        WHERE BookingId IS NOT NULL;

        CREATE TRIGGER IF NOT EXISTS trg_bookings_validate_insert
        BEFORE INSERT ON Bookings
        BEGIN
            SELECT CASE
                WHEN julianday(NEW.DateTo) <= julianday(NEW.DateFrom)
                THEN RAISE(ABORT, 'Дата выезда должна быть позже даты заезда')
            END;

            SELECT CASE
                WHEN NEW.Status IN ('Новое', 'Подтверждено')
                     AND EXISTS (
                         SELECT 1 FROM Bookings b
                         WHERE b.RoomId = NEW.RoomId
                           AND b.Status IN ('Новое', 'Подтверждено')
                           AND julianday(NEW.DateFrom) < julianday(b.DateTo)
                           AND julianday(NEW.DateTo) > julianday(b.DateFrom)
                     )
                THEN RAISE(ABORT, 'Номер уже забронирован на указанный период')
            END;

            SELECT CASE
                WHEN NEW.Status IN ('Новое', 'Подтверждено')
                     AND EXISTS (
                         SELECT 1 FROM Stays s
                         WHERE s.RoomId = NEW.RoomId
                           AND s.Status = 'Проживает'
                           AND julianday(NEW.DateFrom) < julianday(s.PlannedCheckOutDate)
                           AND julianday(NEW.DateTo) > julianday(s.CheckInDate)
                     )
                THEN RAISE(ABORT, 'Номер занят проживающим гостем на указанный период')
            END;
        END;

        CREATE TRIGGER IF NOT EXISTS trg_bookings_validate_update
        BEFORE UPDATE ON Bookings
        BEGIN
            SELECT CASE
                WHEN julianday(NEW.DateTo) <= julianday(NEW.DateFrom)
                THEN RAISE(ABORT, 'Дата выезда должна быть позже даты заезда')
            END;

            SELECT CASE
                WHEN NEW.Status IN ('Новое', 'Подтверждено')
                     AND EXISTS (
                         SELECT 1 FROM Bookings b
                         WHERE b.Id <> NEW.Id
                           AND b.RoomId = NEW.RoomId
                           AND b.Status IN ('Новое', 'Подтверждено')
                           AND julianday(NEW.DateFrom) < julianday(b.DateTo)
                           AND julianday(NEW.DateTo) > julianday(b.DateFrom)
                     )
                THEN RAISE(ABORT, 'Номер уже забронирован на указанный период')
            END;

            SELECT CASE
                WHEN NEW.Status IN ('Новое', 'Подтверждено')
                     AND EXISTS (
                         SELECT 1 FROM Stays s
                         WHERE s.RoomId = NEW.RoomId
                           AND s.Status = 'Проживает'
                           AND julianday(NEW.DateFrom) < julianday(s.PlannedCheckOutDate)
                           AND julianday(NEW.DateTo) > julianday(s.CheckInDate)
                     )
                THEN RAISE(ABORT, 'Номер занят проживающим гостем на указанный период')
            END;
        END;

        CREATE TRIGGER IF NOT EXISTS trg_stays_validate_insert
        BEFORE INSERT ON Stays
        BEGIN
            SELECT CASE
                WHEN julianday(NEW.PlannedCheckOutDate) <= julianday(NEW.CheckInDate)
                THEN RAISE(ABORT, 'Плановая дата выезда должна быть позже даты заезда')
            END;

            SELECT CASE
                WHEN NEW.Status = 'Проживает'
                     AND EXISTS (
                         SELECT 1 FROM Stays s
                         WHERE s.RoomId = NEW.RoomId
                           AND s.Status = 'Проживает'
                           AND julianday(NEW.CheckInDate) < julianday(s.PlannedCheckOutDate)
                           AND julianday(NEW.PlannedCheckOutDate) > julianday(s.CheckInDate)
                     )
                THEN RAISE(ABORT, 'В номере уже проживает гость на указанный период')
            END;
        END;

        CREATE TRIGGER IF NOT EXISTS trg_stays_validate_update
        BEFORE UPDATE ON Stays
        BEGIN
            SELECT CASE
                WHEN julianday(NEW.PlannedCheckOutDate) <= julianday(NEW.CheckInDate)
                THEN RAISE(ABORT, 'Плановая дата выезда должна быть позже даты заезда')
            END;

            SELECT CASE
                WHEN NEW.Status = 'Проживает'
                     AND EXISTS (
                         SELECT 1 FROM Stays s
                         WHERE s.Id <> NEW.Id
                           AND s.RoomId = NEW.RoomId
                           AND s.Status = 'Проживает'
                           AND julianday(NEW.CheckInDate) < julianday(s.PlannedCheckOutDate)
                           AND julianday(NEW.PlannedCheckOutDate) > julianday(s.CheckInDate)
                     )
                THEN RAISE(ABORT, 'В номере уже проживает гость на указанный период')
            END;
        END;

        CREATE TRIGGER IF NOT EXISTS trg_rooms_after_insert
        AFTER INSERT ON Rooms
        BEGIN
            INSERT INTO AuditLog(Action, Entity, EntityId, Details)
            VALUES ('INSERT', 'Rooms', NEW.Id, 'Добавлен номер ' || NEW.Number);
        END;

        CREATE TRIGGER IF NOT EXISTS trg_rooms_after_update
        AFTER UPDATE ON Rooms
        BEGIN
            INSERT INTO AuditLog(Action, Entity, EntityId, Details)
            VALUES ('UPDATE', 'Rooms', NEW.Id, 'Изменен номер ' || NEW.Number);
        END;

        CREATE TRIGGER IF NOT EXISTS trg_bookings_after_insert
        AFTER INSERT ON Bookings
        BEGIN
            INSERT INTO AuditLog(Action, Entity, EntityId, Details)
            VALUES ('INSERT', 'Bookings', NEW.Id, 'Создано бронирование');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_bookings_after_update
        AFTER UPDATE ON Bookings
        BEGIN
            INSERT INTO AuditLog(Action, Entity, EntityId, Details)
            VALUES ('UPDATE', 'Bookings', NEW.Id, 'Обновлено бронирование');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_stays_after_insert
        AFTER INSERT ON Stays
        WHEN NEW.Status = 'Проживает'
        BEGIN
            UPDATE Rooms SET Status = 'Занят' WHERE Id = NEW.RoomId;
            INSERT INTO AuditLog(Action, Entity, EntityId, Details)
            VALUES ('INSERT', 'Stays', NEW.Id, 'Гость заселен');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_stays_after_update
        AFTER UPDATE ON Stays
        BEGIN
            UPDATE Rooms
               SET Status = 'Свободен'
             WHERE Id = OLD.RoomId
               AND NEW.Status = 'Выбыл'
               AND NOT EXISTS (
                   SELECT 1 FROM Stays s
                   WHERE s.RoomId = OLD.RoomId AND s.Status = 'Проживает'
               );

            INSERT OR IGNORE INTO GuestArchive (StayId, GuestId, FullName, RoomNumber, CheckInDate, CheckOutDate, CreatedAt)
            SELECT NEW.Id, g.Id, g.LastName || ' ' || g.FirstName || ' ' || IFNULL(g.MiddleName, ''),
                   r.Number, NEW.CheckInDate, COALESCE(NEW.ActualCheckOutDate, date('now', 'localtime')),
                   datetime('now', 'localtime')
            FROM Guests g
            JOIN Rooms r ON r.Id = NEW.RoomId
            WHERE g.Id = NEW.GuestId AND NEW.Status = 'Выбыл';

            INSERT INTO AuditLog(Action, Entity, EntityId, Details)
            VALUES ('UPDATE', 'Stays', NEW.Id, 'Обновлена карточка проживания');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_services_after_update
        AFTER UPDATE ON Services
        BEGIN
            INSERT INTO AuditLog(Action, Entity, EntityId, Details)
            VALUES ('UPDATE', 'Services', NEW.Id, 'Обновлена услуга ' || NEW.Name);
        END;

        CREATE TRIGGER IF NOT EXISTS trg_payments_after_insert
        AFTER INSERT ON Payments
        BEGIN
            INSERT INTO AuditLog(Action, Entity, EntityId, Details)
            VALUES ('INSERT', 'Payments', NEW.Id, 'Принята оплата на сумму ' || NEW.Amount);
        END;
        """;
}
