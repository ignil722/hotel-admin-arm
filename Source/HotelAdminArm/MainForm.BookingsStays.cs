using System.Data;
using HotelAdminArm.Data;

namespace HotelAdminArm;

public sealed partial class MainForm
{
    private TabPage BuildBookingsTab()
    {
        var page = CreateTabPage("Бронирование");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 6
        };
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _availableFromPicker = DatePicker(DateTime.Today);
        _availableToPicker = DatePicker(DateTime.Today.AddDays(1));
        _availableTypeCombo = Combo();
        _availableCapacityNumeric = CountNumeric(10);
        var findButton = CreateButton("Найти свободные номера");
        findButton.Click += (_, _) => FindAvailableRooms();

        AddField(searchPanel, "Дата заезда", _availableFromPicker, 0);
        AddField(searchPanel, "Дата выезда", _availableToPicker, 1);
        AddField(searchPanel, "Тип", _availableTypeCombo, 2);
        AddField(searchPanel, "Минимум мест", _availableCapacityNumeric, 3);
        searchPanel.Controls.Add(new Label(), 0, 4);
        searchPanel.Controls.Add(findButton, 1, 4);

        _availableRoomsGrid = CreateGrid();
        _availableRoomsGrid.DoubleClick += (_, _) => UseAvailableRoomForBooking();

        _bookingsGrid = CreateGrid();
        _bookingsGrid.SelectionChanged += (_, _) => LoadSelectedBooking();

        var bookingEditor = CreateBookingEditor();
        layout.Controls.Add(searchPanel, 0, 0);
        layout.Controls.Add(_availableRoomsGrid, 0, 1);
        layout.Controls.Add(_bookingsGrid, 1, 0);
        layout.Controls.Add(bookingEditor, 1, 1);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildStaysTab()
    {
        var page = CreateTabPage("Заселение и архив");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 53));

        _pendingBookingsGrid = CreateGrid();
        _staysGrid = CreateGrid();
        _archiveGrid = CreateGrid();

        var pendingPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        pendingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        pendingPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var checkInButton = CreateButton("Заселить выбранную бронь");
        checkInButton.Click += (_, _) => CheckInSelectedBooking();
        pendingPanel.Controls.Add(ButtonsPanel(checkInButton), 0, 0);
        pendingPanel.Controls.Add(_pendingBookingsGrid, 0, 1);

        var stayEditor = CreateStayEditor();
        var activePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        activePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        activePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var checkOutButton = CreateButton("Оформить выезд выбранного проживания");
        checkOutButton.Click += (_, _) => CheckoutSingleStay();
        activePanel.Controls.Add(_staysGrid, 0, 0);
        activePanel.Controls.Add(ButtonsPanel(checkOutButton), 0, 1);

        layout.Controls.Add(pendingPanel, 0, 0);
        layout.Controls.Add(stayEditor, 1, 0);
        layout.Controls.Add(activePanel, 0, 1);
        layout.Controls.Add(_archiveGrid, 1, 1);

        page.Controls.Add(layout);
        return page;
    }

    private TableLayoutPanel CreateBookingEditor()
    {
        _bookingGuestCombo = Combo();
        _bookingRoomCombo = Combo();
        _bookingFromPicker = DatePicker(DateTime.Today);
        _bookingToPicker = DatePicker(DateTime.Today.AddDays(1));
        _bookingStatusCombo = Combo();
        _bookingStatusCombo.DataSource = new[] { "Новое", "Подтверждено", "Заселен", "Отменено" };
        _bookingCommentText = TextBoxMulti();

        var addButton = CreateButton("Создать бронь");
        addButton.Click += (_, _) => SaveBooking(false);
        var updateButton = CreateButton("Сохранить");
        updateButton.Click += (_, _) => SaveBooking(true);
        var cancelButton = CreateButton("Отменить");
        cancelButton.Click += (_, _) => CancelBooking();
        var deleteButton = CreateButton("Удалить");
        deleteButton.Click += (_, _) => DeleteBooking();

        var editor = CreateEditorLayout(7);
        AddField(editor, "Гость", _bookingGuestCombo, 0);
        AddField(editor, "Номер", _bookingRoomCombo, 1);
        AddField(editor, "Заезд", _bookingFromPicker, 2);
        AddField(editor, "Выезд", _bookingToPicker, 3);
        AddField(editor, "Статус", _bookingStatusCombo, 4);
        AddField(editor, "Комментарий", _bookingCommentText, 5);
        editor.Controls.Add(new Label(), 0, 6);
        editor.Controls.Add(ButtonsPanel(addButton, updateButton, cancelButton, deleteButton), 1, 6);
        return editor;
    }

    private TableLayoutPanel CreateStayEditor()
    {
        _stayGuestCombo = Combo();
        _stayRoomCombo = Combo();
        _stayCheckInPicker = DatePicker(DateTime.Today);
        _stayPlannedOutPicker = DatePicker(DateTime.Today.AddDays(1));
        _stayNotesText = TextBoxMulti();

        var addButton = CreateButton("Заселить без брони");
        addButton.Click += (_, _) => CheckInWithoutBooking();

        var editor = CreateEditorLayout(6);
        AddField(editor, "Гость", _stayGuestCombo, 0);
        AddField(editor, "Номер", _stayRoomCombo, 1);
        AddField(editor, "Заезд", _stayCheckInPicker, 2);
        AddField(editor, "Плановый выезд", _stayPlannedOutPicker, 3);
        AddField(editor, "Примечание", _stayNotesText, 4);
        editor.Controls.Add(new Label(), 0, 5);
        editor.Controls.Add(ButtonsPanel(addButton), 1, 5);
        return editor;
    }

    private void RefreshBookings()
    {
        SetGrid(
            _bookingsGrid,
            Database.Query(
                """
                SELECT b.Id, g.LastName || ' ' || g.FirstName AS 'Гость',
                       r.Number AS 'Номер', rt.Name AS 'Тип',
                       b.DateFrom AS 'Заезд', b.DateTo AS 'Выезд',
                       b.Status AS 'Статус', b.Comment AS 'Комментарий'
                FROM Bookings b
                JOIN Guests g ON g.Id = b.GuestId
                JOIN Rooms r ON r.Id = b.RoomId
                JOIN RoomTypes rt ON rt.Id = r.RoomTypeId
                ORDER BY b.DateFrom DESC, b.Id DESC;
                """));

        SetGrid(
            _pendingBookingsGrid,
            Database.Query(
                """
                SELECT b.Id, g.LastName || ' ' || g.FirstName AS 'Гость',
                       r.Number AS 'Номер', b.DateFrom AS 'Заезд',
                       b.DateTo AS 'Выезд', b.Status AS 'Статус'
                FROM Bookings b
                JOIN Guests g ON g.Id = b.GuestId
                JOIN Rooms r ON r.Id = b.RoomId
                WHERE b.Status IN ('Новое', 'Подтверждено')
                ORDER BY b.DateFrom;
                """));
    }

    private void RefreshStays()
    {
        SetGrid(
            _staysGrid,
            Database.Query(
                """
                SELECT s.Id, g.LastName || ' ' || g.FirstName AS 'Гость',
                       r.Number AS 'Номер', s.CheckInDate AS 'Заезд',
                       s.PlannedCheckOutDate AS 'Плановый выезд',
                       IFNULL(s.ActualCheckOutDate, '') AS 'Фактический выезд',
                       s.Status AS 'Статус', s.Notes AS 'Примечание'
                FROM Stays s
                JOIN Guests g ON g.Id = s.GuestId
                JOIN Rooms r ON r.Id = s.RoomId
                ORDER BY s.Status, s.CheckInDate DESC;
                """));

        SetGrid(
            _archiveGrid,
            Database.Query(
                """
                SELECT Id, FullName AS 'Гость', RoomNumber AS 'Номер',
                       CheckInDate AS 'Заезд', CheckOutDate AS 'Выезд',
                       CreatedAt AS 'Дата архивации'
                FROM GuestArchive
                ORDER BY Id DESC;
                """));
    }

    private void FindAvailableRooms()
    {
        RunSafely(() =>
        {
            SetGrid(
                _availableRoomsGrid,
                Database.Query(
                    """
                    SELECT r.Id, r.Number AS 'Номер', rt.Name AS 'Тип',
                           r.Floor AS 'Этаж', r.Capacity AS 'Мест',
                           r.DailyPrice AS 'Цена за сутки'
                    FROM Rooms r
                    JOIN RoomTypes rt ON rt.Id = r.RoomTypeId
                    WHERE r.Status <> 'Ремонт'
                      AND (@typeId = 0 OR r.RoomTypeId = @typeId)
                      AND r.Capacity >= @capacity
                      AND julianday(@dateFrom) < julianday(@dateTo)
                      AND NOT EXISTS (
                          SELECT 1 FROM Bookings b
                          WHERE b.RoomId = r.Id
                            AND b.Status IN ('Новое', 'Подтверждено')
                            AND julianday(@dateFrom) < julianday(b.DateTo)
                            AND julianday(@dateTo) > julianday(b.DateFrom)
                      )
                      AND NOT EXISTS (
                          SELECT 1 FROM Stays s
                          WHERE s.RoomId = r.Id
                            AND s.Status = 'Проживает'
                            AND julianday(@dateFrom) < julianday(s.PlannedCheckOutDate)
                            AND julianday(@dateTo) > julianday(s.CheckInDate)
                      )
                    ORDER BY r.DailyPrice, r.Number;
                    """,
                    Database.P("typeId", ComboId(_availableTypeCombo)),
                    Database.P("capacity", (int)_availableCapacityNumeric.Value),
                    Database.P("dateFrom", DateValue(_availableFromPicker)),
                    Database.P("dateTo", DateValue(_availableToPicker))));
        });
    }

    private void UseAvailableRoomForBooking()
    {
        var roomId = SelectedId(_availableRoomsGrid);
        if (roomId == 0)
        {
            return;
        }

        _bookingRoomCombo.SelectedValue = roomId;
        _bookingFromPicker.Value = _availableFromPicker.Value;
        _bookingToPicker.Value = _availableToPicker.Value;
    }

    private void LoadSelectedBooking()
    {
        if (_loading)
        {
            return;
        }

        var id = SelectedId(_bookingsGrid);
        if (id == 0)
        {
            return;
        }

        var row = FirstRow(Database.Query("SELECT * FROM Bookings WHERE Id = @id;", Database.P("id", id)));
        if (row is null)
        {
            return;
        }

        SelectComboValue(_bookingGuestCombo, row["GuestId"]);
        SelectComboValue(_bookingRoomCombo, row["RoomId"]);
        _bookingFromPicker.Value = ParseDate(row["DateFrom"]);
        _bookingToPicker.Value = ParseDate(row["DateTo"]);
        _bookingStatusCombo.SelectedItem = Convert.ToString(row["Status"]);
        _bookingCommentText.Text = Convert.ToString(row["Comment"]);
    }

    private void SaveBooking(bool update)
    {
        RunSafely(() =>
        {
            var id = SelectedId(_bookingsGrid);
            if (update && id == 0)
            {
                throw new InvalidOperationException("Выберите бронирование для изменения.");
            }

            var sql = update
                ? """
                  UPDATE Bookings
                     SET GuestId = @guestId, RoomId = @roomId, DateFrom = @dateFrom,
                         DateTo = @dateTo, Status = @status, Comment = @comment
                   WHERE Id = @id;
                  """
                : """
                  INSERT INTO Bookings (GuestId, RoomId, DateFrom, DateTo, Status, Comment)
                  VALUES (@guestId, @roomId, @dateFrom, @dateTo, @status, @comment);
                  """;

            var parameters = new List<Microsoft.Data.Sqlite.SqliteParameter>
            {
                Database.P("guestId", ComboId(_bookingGuestCombo)),
                Database.P("roomId", ComboId(_bookingRoomCombo)),
                Database.P("dateFrom", DateValue(_bookingFromPicker)),
                Database.P("dateTo", DateValue(_bookingToPicker)),
                Database.P("status", Convert.ToString(_bookingStatusCombo.SelectedItem)),
                Database.P("comment", _bookingCommentText.Text.Trim())
            };

            if (update)
            {
                parameters.Add(Database.P("id", id));
            }

            Database.Execute(sql, parameters.ToArray());
            RefreshAll();
            FindAvailableRooms();
            MessageBox.Show("Бронирование сохранено.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void CancelBooking()
    {
        RunSafely(() =>
        {
            var id = SelectedId(_bookingsGrid);
            if (id == 0)
            {
                return;
            }

            Database.Execute("UPDATE Bookings SET Status = 'Отменено' WHERE Id = @id;", Database.P("id", id));
            RefreshAll();
            FindAvailableRooms();
        });
    }

    private void DeleteBooking()
    {
        RunSafely(() =>
        {
            var id = SelectedId(_bookingsGrid);
            if (id == 0)
            {
                return;
            }

            if (MessageBox.Show("Удалить выбранное бронирование?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Database.Execute("DELETE FROM Bookings WHERE Id = @id;", Database.P("id", id));
                RefreshAll();
                FindAvailableRooms();
            }
        });
    }

    private void CheckInSelectedBooking()
    {
        RunSafely(() =>
        {
            var bookingId = SelectedId(_pendingBookingsGrid);
            if (bookingId == 0)
            {
                throw new InvalidOperationException("Выберите бронь для заселения.");
            }

            var row = FirstRow(Database.Query("SELECT * FROM Bookings WHERE Id = @id;", Database.P("id", bookingId)));
            if (row is null)
            {
                return;
            }

            Database.ExecuteInTransaction((connection, transaction) =>
            {
                Database.Insert(
                    connection,
                    transaction,
                    """
                    INSERT INTO Stays (GuestId, RoomId, BookingId, CheckInDate, PlannedCheckOutDate, Status, Notes)
                    VALUES (@guestId, @roomId, @bookingId, @checkInDate, @plannedCheckOutDate, 'Проживает', @notes);
                    """,
                    Database.P("guestId", row["GuestId"]),
                    Database.P("roomId", row["RoomId"]),
                    Database.P("bookingId", bookingId),
                    Database.P("checkInDate", DateTime.Today),
                    Database.P("plannedCheckOutDate", row["DateTo"]),
                    Database.P("notes", "Заселение выполнено на основании бронирования."));

                Database.Execute(
                    connection,
                    transaction,
                    "UPDATE Bookings SET Status = 'Заселен' WHERE Id = @id;",
                    Database.P("id", bookingId));
            });

            RefreshAll();
            FindAvailableRooms();
            MessageBox.Show("Гость заселен.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void CheckInWithoutBooking()
    {
        RunSafely(() =>
        {
            Database.Execute(
                """
                INSERT INTO Stays (GuestId, RoomId, BookingId, CheckInDate, PlannedCheckOutDate, Status, Notes)
                VALUES (@guestId, @roomId, NULL, @checkInDate, @plannedCheckOutDate, 'Проживает', @notes);
                """,
                Database.P("guestId", ComboId(_stayGuestCombo)),
                Database.P("roomId", ComboId(_stayRoomCombo)),
                Database.P("checkInDate", DateValue(_stayCheckInPicker)),
                Database.P("plannedCheckOutDate", DateValue(_stayPlannedOutPicker)),
                Database.P("notes", _stayNotesText.Text.Trim()));

            RefreshAll();
            FindAvailableRooms();
            MessageBox.Show("Карточка регистрации создана.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void CheckoutSingleStay()
    {
        var stayId = SelectedId(_staysGrid);
        if (stayId == 0)
        {
            return;
        }

        PayForStays(new[] { stayId });
    }
}
