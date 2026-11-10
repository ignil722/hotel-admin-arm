using Microsoft.Data.Sqlite;

namespace HotelAdminArm.Data;

internal static partial class Database
{
    private static void UpgradeOrderRules(SqliteConnection c, SqliteTransaction tx)
    {
        if (Scalar<long>(c,tx,"SELECT count(*) FROM pragma_table_info('Services') WHERE name='MealKind'")>0) return;
        Execute(c,tx,"""
            ALTER TABLE Services ADD COLUMN MealKind TEXT NOT NULL DEFAULT 'None' CHECK(MealKind IN('None','Breakfast','Lunch','Dinner'));
            ALTER TABLE Services ADD COLUMN RepeatRule TEXT NOT NULL DEFAULT 'Repeatable' CHECK(RepeatRule IN('Repeatable','Daily','Once'));
            ALTER TABLE ServiceOrders ADD COLUMN GuestId INTEGER REFERENCES Guests(Id) ON DELETE RESTRICT;
            ALTER TABLE ServiceOrders ADD COLUMN GuestNameSnapshot TEXT NOT NULL DEFAULT '';
            ALTER TABLE ServiceOrders ADD COLUMN MealKind TEXT NOT NULL DEFAULT 'None' CHECK(MealKind IN('None','Breakfast','Lunch','Dinner'));
            ALTER TABLE ServiceOrders ADD COLUMN RepeatRule TEXT NOT NULL DEFAULT 'Repeatable' CHECK(RepeatRule IN('Repeatable','Daily','Once'));
            ALTER TABLE ServiceOrders ADD COLUMN IsLegacyRule INTEGER NOT NULL DEFAULT 0 CHECK(IsLegacyRule IN(0,1));
            UPDATE Services SET MealKind=hotel_meal_kind(Name);
            UPDATE Services SET RepeatRule=CASE WHEN MealKind<>'None' OR trim(Name)='Парковка' THEN 'Daily' WHEN trim(Name)='Поздний выезд' THEN 'Once' ELSE 'Repeatable' END;
            UPDATE ServiceOrders SET IsLegacyRule=1,
                MealKind=CASE WHEN hotel_meal_kind(NameSnapshot)<>'None' THEN hotel_meal_kind(NameSnapshot) ELSE (SELECT MealKind FROM Services WHERE Id=ServiceId) END,
                RepeatRule=(SELECT RepeatRule FROM Services WHERE Id=ServiceId);
            UPDATE ServiceOrders SET RepeatRule='Daily' WHERE MealKind<>'None';
            UPDATE ServiceOrders SET GuestId=(SELECT GuestId FROM StayGuests WHERE StayId=ServiceOrders.StayId),
                GuestNameSnapshot=(SELECT g.LastName||' '||g.FirstName||' '||IFNULL(g.MiddleName,'') FROM StayGuests sg JOIN Guests g ON g.Id=sg.GuestId WHERE sg.StayId=ServiceOrders.StayId)
                WHERE MealKind<>'None' AND Quantity=1 AND (SELECT count(*) FROM StayGuests WHERE StayId=ServiceOrders.StayId)=1;
            CREATE INDEX IX_Orders_MealGuestDate ON ServiceOrders(GuestId,MealKind,OrderDate);
            CREATE INDEX IX_Orders_ServiceStayDate ON ServiceOrders(StayId,ServiceId,OrderDate);
            """);
    }

    private static void InstallOrderRules(SqliteConnection c, SqliteTransaction tx)
    {
        Execute(c,tx,"""
            CREATE TRIGGER service_rules_insert BEFORE INSERT ON Services BEGIN
                SELECT CASE WHEN hotel_meal_kind(NEW.Name)<>'None' AND NEW.MealKind<>hotel_meal_kind(NEW.Name) THEN RAISE(ABORT,'Выберите соответствующую названию категорию питания') END;
                SELECT CASE WHEN NEW.MealKind<>'None' AND NEW.RepeatRule<>'Daily' THEN RAISE(ABORT,'Питание: один раз в день на гостя') END;
            END;
            CREATE TRIGGER service_rules_update BEFORE UPDATE ON Services BEGIN
                SELECT CASE WHEN hotel_meal_kind(NEW.Name)<>'None' AND NEW.MealKind<>hotel_meal_kind(NEW.Name) THEN RAISE(ABORT,'Выберите соответствующую названию категорию питания') END;
                SELECT CASE WHEN NEW.MealKind<>'None' AND NEW.RepeatRule<>'Daily' THEN RAISE(ABORT,'Питание: один раз в день на гостя') END;
                SELECT CASE WHEN (NEW.MealKind<>OLD.MealKind OR NEW.RepeatRule<>OLD.RepeatRule) AND EXISTS(SELECT 1 FROM ServiceOrders WHERE ServiceId=OLD.Id)
                    THEN RAISE(ABORT,'Услуга уже использована: для другой категории или правила создайте новую услугу') END;
            END;
            CREATE TRIGGER order_rules_insert BEFORE INSERT ON ServiceOrders BEGIN
                SELECT CASE WHEN NEW.IsLegacyRule<>0 OR (NEW.MealKind='None' AND NEW.OrderDate<>hotel_today()) OR NEW.Quantity NOT BETWEEN 1 AND 100
                    THEN RAISE(ABORT,'Заказ: проверьте дату и количество от 1 до 100') END;
                SELECT CASE WHEN NEW.MealKind<>'None' AND (NEW.OrderDate<hotel_today() OR NEW.OrderDate>=(SELECT PlannedCheckOutDate FROM Stays WHERE Id=NEW.StayId))
                    THEN RAISE(ABORT,'Выберите дату питания от сегодня до дня перед выездом; при необходимости продлите проживание') END;
                SELECT CASE WHEN NEW.MealKind<>'None' AND NEW.OrderDate=hotel_today() AND time(hotel_now())>=hotel_meal_deadline(NEW.MealKind)
                    THEN RAISE(ABORT,'Приём заказов на это питание сегодня завершён; выберите следующий день проживания') END;
                SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM Services WHERE Id=NEW.ServiceId AND MealKind=NEW.MealKind AND RepeatRule=NEW.RepeatRule AND Name=NEW.NameSnapshot AND PriceKopecks=NEW.UnitPriceKopecks)
                    THEN RAISE(ABORT,'Категория, правило и цена должны соответствовать услуге') END;
                SELECT CASE WHEN NEW.MealKind<>'None' AND (NEW.Quantity<>1 OR NEW.GuestId IS NULL OR trim(NEW.GuestNameSnapshot)='' OR NOT EXISTS(SELECT 1 FROM StayGuests sg JOIN Guests g ON g.Id=sg.GuestId WHERE sg.StayId=NEW.StayId AND sg.GuestId=NEW.GuestId AND g.IsActive=1 AND NEW.GuestNameSnapshot=g.LastName||' '||g.FirstName||' '||IFNULL(g.MiddleName,'')))
                    THEN RAISE(ABORT,'Выберите проживающего гостя; одна порция питания на гостя') END;
                SELECT CASE WHEN NEW.MealKind='None' AND (NEW.GuestId IS NOT NULL OR NEW.GuestNameSnapshot<>'') THEN RAISE(ABORT,'Непищевая услуга относится к проживанию') END;
                SELECT CASE WHEN NEW.MealKind<>'None' AND EXISTS(SELECT 1 FROM ServiceOrders o WHERE o.MealKind=NEW.MealKind AND o.OrderDate=NEW.OrderDate AND
                    (o.GuestId=NEW.GuestId OR (o.GuestId IS NULL AND o.StayId=NEW.StayId)))
                    THEN RAISE(ABORT,'Это питание уже заказано на выбранную дату; проверьте также старые заказы') END;
                SELECT CASE WHEN NEW.MealKind='None' AND NEW.RepeatRule<>'Repeatable' AND (NEW.Quantity<>1 OR EXISTS(SELECT 1 FROM ServiceOrders o WHERE o.StayId=NEW.StayId AND o.ServiceId=NEW.ServiceId AND (NEW.RepeatRule='Once' OR o.OrderDate=NEW.OrderDate)))
                    THEN RAISE(ABORT,'Превышен лимит услуги для проживания') END;
            END;
            CREATE TRIGGER order_rules_history BEFORE UPDATE ON ServiceOrders BEGIN
                SELECT RAISE(ABORT,'Заказ защищён: удалите ошибочный заказ из открытого счёта и добавьте заново');
            END;
            """);
    }
}
