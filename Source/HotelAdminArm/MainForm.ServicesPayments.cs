using HotelAdminArm.Data;

namespace HotelAdminArm;

public sealed partial class MainForm
{
    private TabPage BuildServicesTab()
    {
        var page = CreateTabPage("Услуги");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        _servicesGrid = CreateGrid();
        _servicesGrid.SelectionChanged += (_, _) => LoadSelectedService();
        _ordersGrid = CreateGrid();

        _serviceNameText = TextBoxSingle();
        _servicePriceNumeric = MoneyNumeric();
        _serviceIsActiveCheck = new CheckBox { Text = "Услуга активна", Checked = true, AutoSize = true };

        var addServiceButton = CreateButton("Добавить услугу");
        addServiceButton.Click += (_, _) => SaveService(false);
        var updateServiceButton = CreateButton("Сохранить");
        updateServiceButton.Click += (_, _) => SaveService(true);
        var deleteServiceButton = CreateButton("Удалить");
        deleteServiceButton.Click += (_, _) => DeleteService();

        var serviceEditor = CreateEditorLayout(4);
        AddField(serviceEditor, "Название", _serviceNameText, 0);
        AddField(serviceEditor, "Цена", _servicePriceNumeric, 1);
        AddField(serviceEditor, "Статус", _serviceIsActiveCheck, 2);
        serviceEditor.Controls.Add(new Label(), 0, 3);
        serviceEditor.Controls.Add(ButtonsPanel(addServiceButton, updateServiceButton, deleteServiceButton), 1, 3);

        _orderStayCombo = Combo();
        _orderServiceCombo = Combo();
        _orderQuantityNumeric = CountNumeric(100);
        var addOrderButton = CreateButton("Добавить услугу к проживанию");
        addOrderButton.Click += (_, _) => AddServiceOrder();
        var deleteOrderButton = CreateButton("Удалить запись услуги");
        deleteOrderButton.Click += (_, _) => DeleteServiceOrder();

        var orderEditor = CreateEditorLayout(4);
        AddField(orderEditor, "Проживание", _orderStayCombo, 0);
        AddField(orderEditor, "Услуга", _orderServiceCombo, 1);
        AddField(orderEditor, "Количество", _orderQuantityNumeric, 2);
        orderEditor.Controls.Add(new Label(), 0, 3);
        orderEditor.Controls.Add(ButtonsPanel(addOrderButton, deleteOrderButton), 1, 3);

        layout.Controls.Add(_servicesGrid, 0, 0);
        layout.Controls.Add(serviceEditor, 1, 0);
        layout.Controls.Add(_ordersGrid, 0, 1);
        layout.Controls.Add(orderEditor, 1, 1);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPaymentsTab()
    {
        var page = CreateTabPage("Оплата");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        _paymentStaysGrid = CreateGrid(true);
        _paymentsGrid = CreateGrid();

        _paymentPayerCombo = Combo();
        _paymentMethodCombo = Combo();
        _paymentMethodCombo.DataSource = new[] { "Наличные", "Банковская карта", "Перевод" };
        _paymentTotalLabel = new Label
        {
            Text = "Итого: 0 руб.",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            Margin = new Padding(0, 10, 0, 10)
        };

        var calculateButton = CreateButton("Рассчитать выбранные проживания");
        calculateButton.Click += (_, _) => CalculateSelectedPayment();
        var payButton = CreateButton("Принять оплату и закрыть проживание");
        payButton.Click += (_, _) => PaySelectedStays();

        var editor = CreateEditorLayout(5);
        AddField(editor, "Плательщик", _paymentPayerCombo, 0);
        AddField(editor, "Способ оплаты", _paymentMethodCombo, 1);
        editor.Controls.Add(new Label(), 0, 2);
        editor.Controls.Add(_paymentTotalLabel, 1, 2);
        editor.Controls.Add(new Label(), 0, 3);
        editor.Controls.Add(ButtonsPanel(calculateButton, payButton), 1, 3);
        editor.Controls.Add(
            new Label
            {
                Text = "Для оплаты нескольких номеров выделите несколько строк в таблице активных проживаний.",
                Dock = DockStyle.Fill,
                AutoSize = true
            },
            1,
            4);

        layout.Controls.Add(_paymentStaysGrid, 0, 0);
        layout.Controls.Add(editor, 1, 0);
        layout.Controls.Add(_paymentsGrid, 0, 1);
        layout.SetColumnSpan(_paymentsGrid, 2);

        page.Controls.Add(layout);
        return page;
    }

    private void RefreshServices()
    {
        SetGrid(
            _servicesGrid,
            Database.Query(
                """
                SELECT Id, Name AS 'Услуга', Price AS 'Цена',
                       CASE IsActive WHEN 1 THEN 'Да' ELSE 'Нет' END AS 'Активна'
                FROM Services
                ORDER BY Name;
                """));
    }

    private void RefreshOrders()
    {
        SetGrid(
            _ordersGrid,
            Database.Query(
                """
                SELECT so.Id, r.Number AS 'Номер',
                       g.LastName || ' ' || g.FirstName AS 'Гость',
                       sv.Name AS 'Услуга', so.Quantity AS 'Кол-во',
                       sv.Price AS 'Цена', so.Quantity * sv.Price AS 'Сумма',
                       so.OrderDate AS 'Дата'
                FROM ServiceOrders so
                JOIN Stays s ON s.Id = so.StayId
                JOIN Rooms r ON r.Id = s.RoomId
                JOIN Guests g ON g.Id = s.GuestId
                JOIN Services sv ON sv.Id = so.ServiceId
                ORDER BY so.Id DESC;
                """));
    }

    private void RefreshPayments()
    {
        SetGrid(
            _paymentStaysGrid,
            Database.Query(
                """
                SELECT s.Id, r.Number AS 'Номер',
                       g.LastName || ' ' || g.FirstName AS 'Гость',
                       s.CheckInDate AS 'Заезд',
                       s.PlannedCheckOutDate AS 'Плановый выезд',
                       r.DailyPrice AS 'Цена/сутки',
                       IFNULL((
                           SELECT SUM(so.Quantity * sv.Price)
                           FROM ServiceOrders so
                           JOIN Services sv ON sv.Id = so.ServiceId
                           WHERE so.StayId = s.Id
                       ), 0) AS 'Услуги'
                FROM Stays s
                JOIN Rooms r ON r.Id = s.RoomId
                JOIN Guests g ON g.Id = s.GuestId
                WHERE s.Status = 'Проживает'
                ORDER BY r.Number;
                """));

        SetGrid(
            _paymentsGrid,
            Database.Query(
                """
                SELECT p.Id, p.PaymentDate AS 'Дата',
                       g.LastName || ' ' || g.FirstName AS 'Плательщик',
                       p.Amount AS 'Сумма', p.Method AS 'Способ', p.Comment AS 'Комментарий'
                FROM Payments p
                JOIN Guests g ON g.Id = p.PayerGuestId
                ORDER BY p.Id DESC;
                """));

        _paymentTotalLabel.Text = "Итого: 0 руб.";
    }

    private void LoadSelectedService()
    {
        if (_loading)
        {
            return;
        }

        var id = SelectedId(_servicesGrid);
        if (id == 0)
        {
            return;
        }

        var row = FirstRow(Database.Query("SELECT * FROM Services WHERE Id = @id;", Database.P("id", id)));
        if (row is null)
        {
            return;
        }

        _serviceNameText.Text = Convert.ToString(row["Name"]);
        _servicePriceNumeric.Value = Convert.ToDecimal(row["Price"]);
        _serviceIsActiveCheck.Checked = Convert.ToInt32(row["IsActive"]) == 1;
    }

    private void SaveService(bool update)
    {
        RunSafely(() =>
        {
            if (string.IsNullOrWhiteSpace(_serviceNameText.Text))
            {
                throw new InvalidOperationException("Введите название услуги.");
            }

            var id = SelectedId(_servicesGrid);
            if (update && id == 0)
            {
                throw new InvalidOperationException("Выберите услугу для изменения.");
            }

            if (update)
            {
                Database.Execute(
                    """
                    UPDATE Services
                       SET Name = @name, Price = @price, IsActive = @isActive
                     WHERE Id = @id;
                    """,
                    Database.P("name", _serviceNameText.Text.Trim()),
                    Database.P("price", _servicePriceNumeric.Value),
                    Database.P("isActive", _serviceIsActiveCheck.Checked ? 1 : 0),
                    Database.P("id", id));
            }
            else
            {
                Database.Execute(
                    "INSERT INTO Services (Name, Price, IsActive) VALUES (@name, @price, @isActive);",
                    Database.P("name", _serviceNameText.Text.Trim()),
                    Database.P("price", _servicePriceNumeric.Value),
                    Database.P("isActive", _serviceIsActiveCheck.Checked ? 1 : 0));
            }

            RefreshAll();
            MessageBox.Show("Услуга сохранена.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void DeleteService()
    {
        RunSafely(() =>
        {
            var id = SelectedId(_servicesGrid);
            if (id == 0)
            {
                return;
            }

            if (MessageBox.Show("Удалить выбранную услугу?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Database.Execute("DELETE FROM Services WHERE Id = @id;", Database.P("id", id));
                RefreshAll();
            }
        });
    }

    private void AddServiceOrder()
    {
        RunSafely(() =>
        {
            Database.Execute(
                """
                INSERT INTO ServiceOrders (StayId, ServiceId, Quantity, OrderDate)
                VALUES (@stayId, @serviceId, @quantity, @orderDate);
                """,
                Database.P("stayId", ComboId(_orderStayCombo)),
                Database.P("serviceId", ComboId(_orderServiceCombo)),
                Database.P("quantity", (int)_orderQuantityNumeric.Value),
                Database.P("orderDate", DateTime.Today));

            RefreshAll();
            MessageBox.Show("Услуга добавлена к проживанию.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void DeleteServiceOrder()
    {
        RunSafely(() =>
        {
            var id = SelectedId(_ordersGrid);
            if (id == 0)
            {
                return;
            }

            Database.Execute("DELETE FROM ServiceOrders WHERE Id = @id;", Database.P("id", id));
            RefreshAll();
        });
    }

    private void CalculateSelectedPayment()
    {
        var stayIds = SelectedStayIdsForPayment();
        var total = CalculateStaysTotal(stayIds, DateTime.Today);
        _paymentTotalLabel.Text = $"Итого: {total:N2} руб.";
    }

    private void PaySelectedStays()
    {
        var stayIds = SelectedStayIdsForPayment();
        PayForStays(stayIds);
    }

    private int[] SelectedStayIdsForPayment()
    {
        return _paymentStaysGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Where(row => row.Cells["Id"].Value is not null)
            .Select(row => Convert.ToInt32(row.Cells["Id"].Value))
            .Distinct()
            .ToArray();
    }

    private void PayForStays(int[] stayIds)
    {
        RunSafely(() =>
        {
            if (stayIds.Length == 0)
            {
                throw new InvalidOperationException("Выберите проживание для оплаты.");
            }

            var total = CalculateStaysTotal(stayIds, DateTime.Today);
            if (total <= 0)
            {
                throw new InvalidOperationException("Сумма оплаты должна быть больше нуля.");
            }

            if (MessageBox.Show(
                    $"Принять оплату {total:N2} руб. и закрыть выбранные проживания?",
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            Database.ExecuteInTransaction((connection, transaction) =>
            {
                var paymentId = Database.Insert(
                    connection,
                    transaction,
                    """
                    INSERT INTO Payments (PayerGuestId, PaymentDate, Amount, Method, Comment)
                    VALUES (@payerGuestId, @paymentDate, @amount, @method, @comment);
                    """,
                    Database.P("payerGuestId", ComboId(_paymentPayerCombo)),
                    Database.P("paymentDate", DateTime.Today),
                    Database.P("amount", total),
                    Database.P("method", Convert.ToString(_paymentMethodCombo.SelectedItem)),
                    Database.P("comment", $"Оплата проживаний: {string.Join(", ", stayIds)}"));

                foreach (var stayId in stayIds)
                {
                    var stayTotal = CalculateStayTotal(stayId, DateTime.Today, connection, transaction);
                    Database.Execute(
                        connection,
                        transaction,
                        "INSERT INTO PaymentStays (PaymentId, StayId, Amount) VALUES (@paymentId, @stayId, @amount);",
                        Database.P("paymentId", paymentId),
                        Database.P("stayId", stayId),
                        Database.P("amount", stayTotal));

                    Database.Execute(
                        connection,
                        transaction,
                        """
                        UPDATE Stays
                           SET Status = 'Выбыл', ActualCheckOutDate = @actualCheckOutDate
                         WHERE Id = @stayId AND Status = 'Проживает';
                        """,
                        Database.P("actualCheckOutDate", DateTime.Today),
                        Database.P("stayId", stayId));
                }
            });

            RefreshAll();
            FindAvailableRooms();
            MessageBox.Show("Оплата проведена, данные гостя перенесены в архив.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private static decimal CalculateStaysTotal(int[] stayIds, DateTime checkoutDate)
    {
        decimal total = 0;
        foreach (var stayId in stayIds)
        {
            total += CalculateStayTotal(stayId, checkoutDate, null, null);
        }

        return total;
    }

    private static decimal CalculateStayTotal(
        int stayId,
        DateTime checkoutDate,
        Microsoft.Data.Sqlite.SqliteConnection? connection,
        Microsoft.Data.Sqlite.SqliteTransaction? transaction)
    {
        const string sql = """
            SELECT
                MAX(1, CAST(julianday(@checkoutDate) - julianday(s.CheckInDate) AS INTEGER)) * r.DailyPrice
                + IFNULL((
                    SELECT SUM(so.Quantity * sv.Price)
                    FROM ServiceOrders so
                    JOIN Services sv ON sv.Id = so.ServiceId
                    WHERE so.StayId = s.Id
                ), 0) AS Total
            FROM Stays s
            JOIN Rooms r ON r.Id = s.RoomId
            WHERE s.Id = @stayId;
            """;

        object value;
        if (connection is null)
        {
            value = Database.Scalar<double>(
                sql,
                Database.P("checkoutDate", checkoutDate),
                Database.P("stayId", stayId));
        }
        else
        {
            value = Database.Scalar<double>(
                connection,
                transaction,
                sql,
                Database.P("checkoutDate", checkoutDate),
                Database.P("stayId", stayId));
        }

        return Convert.ToDecimal(value);
    }
}
