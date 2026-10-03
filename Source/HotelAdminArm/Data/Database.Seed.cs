using HotelAdminArm;
using Microsoft.Data.Sqlite;

namespace HotelAdminArm.Data;

internal static partial class Database
{
    private static void Seed(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        try
        {
            SeedRoomTypes(connection, transaction);
            SeedRooms(connection, transaction);
            SeedGuests(connection, transaction);
            SeedServices(connection, transaction);
            SeedBookings(connection, transaction);
            SeedStays(connection, transaction);
            SeedServiceOrders(connection, transaction);
            SeedPayments(connection, transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void SeedRoomTypes(SqliteConnection connection, SqliteTransaction transaction)
    {
        var types = new[]
        {
            ("Люкс", "Многокомнатный номер с высоким уровнем сервиса и комфортности."),
            ("Полулюкс", "Номер повышенной комфортности меньшей площади."),
            ("Одноместный", "Номер для одного гостя с базовым уровнем сервиса."),
            ("Двухместный", "Номер для двух гостей с базовым уровнем сервиса.")
        };

        foreach (var type in types)
        {
            Insert(
                connection,
                transaction,
                "INSERT INTO RoomTypes (Name, Description) VALUES (@name, @description);",
                P("name", type.Item1),
                P("description", type.Item2));
        }
    }

    private static void SeedRooms(SqliteConnection connection, SqliteTransaction transaction)
    {
        for (var floor = 1; floor <= 4; floor++)
        {
            for (var index = 1; index <= 5; index++)
            {
                var number = $"{floor}0{index}";
                var ordinal = (floor - 1) * 5 + index;
                var typeId = ordinal switch
                {
                    <= 4 => 1,
                    <= 9 => 2,
                    <= 14 => 3,
                    _ => 4
                };

                var capacity = typeId switch
                {
                    1 => 4,
                    2 => 3,
                    3 => 1,
                    _ => 2
                };

                var dailyPrice = typeId switch
                {
                    1 => 9200 + ordinal * 110,
                    2 => 6100 + ordinal * 90,
                    3 => 3100 + ordinal * 70,
                    _ => 4300 + ordinal * 80
                };

                Insert(
                    connection,
                    transaction,
                    """
                    INSERT INTO Rooms (Number, RoomTypeId, Floor, Capacity, DailyPrice, Status, Notes, Photo)
                    VALUES (@number, @roomTypeId, @floor, @capacity, @dailyPrice, 'Свободен', @notes, @photo);
                    """,
                    P("number", number),
                    P("roomTypeId", typeId),
                    P("floor", floor),
                    P("capacity", capacity),
                    P("dailyPrice", dailyPrice),
                    P("notes", "Демонстрационный номер для курсовой работы."),
                    P("photo", CreateRoomImage(number, typeId)));
            }
        }
    }

    private static void SeedGuests(SqliteConnection connection, SqliteTransaction transaction)
    {
        var lastNames = new[]
        {
            "Иванов", "Петрова", "Сидоров", "Кузнецова", "Смирнов", "Морозова",
            "Волков", "Федорова", "Орлов", "Никитина", "Соколова", "Попов",
            "Лебедева", "Егоров", "Зайцева", "Павлов", "Медведева", "Громов",
            "Белова", "Комаров", "Андреева", "Борисов", "Васильева", "Михайлов"
        };
        var firstNames = new[]
        {
            "Сергей", "Анна", "Алексей", "Мария", "Дмитрий", "Елена",
            "Илья", "Ольга", "Максим", "Дарья", "Ксения", "Владимир",
            "Наталья", "Артем", "Ирина", "Георгий", "Полина", "Кирилл",
            "Юлия", "Никита", "Вероника", "Павел", "Татьяна", "Роман"
        };
        var middleNames = new[]
        {
            "Петрович", "Сергеевна", "Игоревич", "Олеговна", "Андреевич", "Викторовна",
            "Николаевич", "Павловна", "Романович", "Алексеевна", "Денисовна", "Евгеньевич",
            "Ильинична", "Станиславович", "Михайловна", "Валерьевич", "Руслановна", "Дмитриевич",
            "Анатольевна", "Олегович", "Петровна", "Семенович", "Юрьевна", "Александрович"
        };

        for (var i = 0; i < lastNames.Length; i++)
        {
            Insert(
                connection,
                transaction,
                """
                INSERT INTO Guests (LastName, FirstName, MiddleName, BirthDate, Passport, Phone, Email, Address)
                VALUES (@lastName, @firstName, @middleName, @birthDate, @passport, @phone, @email, @address);
                """,
                P("lastName", lastNames[i]),
                P("firstName", firstNames[i]),
                P("middleName", middleNames[i]),
                P("birthDate", DateTime.Today.AddYears(-24 - i).AddDays(i * 9)),
                P("passport", $"45{12 + i:00} {345678 + i:000000}"),
                P("phone", $"+7 920 100-10-{i + 1:00}"),
                P("email", $"guest{i + 1:00}@example.ru"),
                P("address", $"г. Муром, демонстрационная улица, д. {i + 1}"));
        }
    }

    private static void SeedServices(SqliteConnection connection, SqliteTransaction transaction)
    {
        var services = new[]
        {
            ("Завтрак", 650),
            ("Обед", 900),
            ("Ужин", 850),
            ("Телефонная связь", 180),
            ("Стирка одежды", 450),
            ("Глажка одежды", 300),
            ("Трансфер", 1300),
            ("Мини-бар", 700),
            ("Парковка", 500),
            ("Поздний выезд", 1800)
        };

        foreach (var service in services)
        {
            Insert(
                connection,
                transaction,
                "INSERT INTO Services (Name, Price, IsActive) VALUES (@name, @price, 1);",
                P("name", service.Item1),
                P("price", service.Item2));
        }
    }

    private static void SeedBookings(SqliteConnection connection, SqliteTransaction transaction)
    {
        for (var i = 0; i < 10; i++)
        {
            var arrival = DateTime.Today.AddDays(8 + i);
            var departure = arrival.AddDays(2 + i % 3);

            Insert(
                connection,
                transaction,
                """
                INSERT INTO Bookings (GuestId, RoomId, DateFrom, DateTo, Status, Comment)
                VALUES (@guestId, @roomId, @dateFrom, @dateTo, @status, @comment);
                """,
                P("guestId", i + 1),
                P("roomId", i + 1),
                P("dateFrom", arrival),
                P("dateTo", departure),
                P("status", i % 2 == 0 ? "Подтверждено" : "Новое"),
                P("comment", "Первичное бронирование по телефону."));
        }
    }

    private static void SeedStays(SqliteConnection connection, SqliteTransaction transaction)
    {
        for (var i = 0; i < 8; i++)
        {
            var checkIn = DateTime.Today.AddDays(-1 - i % 3);
            var plannedCheckOut = DateTime.Today.AddDays(2 + i % 4);

            Insert(
                connection,
                transaction,
                """
                INSERT INTO Stays (GuestId, RoomId, BookingId, CheckInDate, PlannedCheckOutDate, Status, Notes)
                VALUES (@guestId, @roomId, NULL, @checkInDate, @plannedCheckOutDate, 'Проживает', @notes);
                """,
                P("guestId", i + 11),
                P("roomId", i + 11),
                P("checkInDate", checkIn),
                P("plannedCheckOutDate", plannedCheckOut),
                P("notes", "Карточка регистрации заполнена администратором."));
        }

        for (var i = 0; i < 5; i++)
        {
            var checkIn = DateTime.Today.AddDays(-35 - i * 2);
            var actualCheckOut = checkIn.AddDays(2 + i % 2);
            var stayId = Insert(
                connection,
                transaction,
                """
                INSERT INTO Stays (GuestId, RoomId, BookingId, CheckInDate, PlannedCheckOutDate, ActualCheckOutDate, Status, Notes)
                VALUES (@guestId, @roomId, NULL, @checkInDate, @plannedCheckOutDate, @actualCheckOutDate, 'Выбыл', @notes);
                """,
                P("guestId", i + 19),
                P("roomId", i + 1),
                P("checkInDate", checkIn),
                P("plannedCheckOutDate", actualCheckOut),
                P("actualCheckOutDate", actualCheckOut),
                P("notes", "Архивная запись демонстрационного выезда."));

            Execute(
                connection,
                transaction,
                """
                INSERT INTO GuestArchive (StayId, GuestId, FullName, RoomNumber, CheckInDate, CheckOutDate, CreatedAt)
                SELECT s.Id, g.Id, g.LastName || ' ' || g.FirstName || ' ' || IFNULL(g.MiddleName, ''),
                       r.Number, s.CheckInDate, s.ActualCheckOutDate, datetime('now', 'localtime')
                FROM Stays s
                JOIN Guests g ON g.Id = s.GuestId
                JOIN Rooms r ON r.Id = s.RoomId
                WHERE s.Id = @stayId;
                """,
                P("stayId", stayId));
        }
    }

    private static void SeedServiceOrders(SqliteConnection connection, SqliteTransaction transaction)
    {
        for (var stayId = 1; stayId <= 8; stayId++)
        {
            Insert(
                connection,
                transaction,
                """
                INSERT INTO ServiceOrders (StayId, ServiceId, Quantity, OrderDate)
                VALUES (@stayId, @serviceId, @quantity, @orderDate);
                """,
                P("stayId", stayId),
                P("serviceId", 1 + stayId % 5),
                P("quantity", 1 + stayId % 3),
                P("orderDate", DateTime.Today.AddDays(-stayId % 3)));
        }
    }

    private static void SeedPayments(SqliteConnection connection, SqliteTransaction transaction)
    {
        for (var i = 0; i < 3; i++)
        {
            var amount = 6400 + i * 1250;
            var paymentId = Insert(
                connection,
                transaction,
                """
                INSERT INTO Payments (PayerGuestId, PaymentDate, Amount, Method, Comment)
                VALUES (@payerGuestId, @paymentDate, @amount, @method, @comment);
                """,
                P("payerGuestId", 19 + i),
                P("paymentDate", DateTime.Today.AddDays(-20 - i)),
                P("amount", amount),
                P("method", i % 2 == 0 ? "Наличные" : "Банковская карта"),
                P("comment", "Демонстрационная оплата архивного проживания."));

            Execute(
                connection,
                transaction,
                "INSERT INTO PaymentStays (PaymentId, StayId, Amount) VALUES (@paymentId, @stayId, @amount);",
                P("paymentId", paymentId),
                P("stayId", 9 + i),
                P("amount", amount));
        }
    }

    private static byte[] CreateRoomImage(string roomNumber, int roomTypeId)
    {
        return HotelBranding.CreateRoomImage(roomNumber, roomTypeId);
    }
}
