using System.Data;
using System.Globalization;
using HotelAdminArm.Data;

namespace HotelAdminArm;

public sealed partial class MainForm : Form
{
    private const string DateFormat = "yyyy-MM-dd";
    private bool _loading;
    private byte[]? _selectedRoomPhoto;

    private readonly TabControl _tabs = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _databaseStatus = new();

    private Label _statsLabel = null!;
    private DataGridView _auditGrid = null!;

    private DataGridView _roomsGrid = null!;
    private TextBox _roomNumberText = null!;
    private ComboBox _roomTypeCombo = null!;
    private NumericUpDown _roomFloorNumeric = null!;
    private NumericUpDown _roomCapacityNumeric = null!;
    private NumericUpDown _roomPriceNumeric = null!;
    private ComboBox _roomStatusCombo = null!;
    private TextBox _roomNotesText = null!;
    private PictureBox _roomPhotoBox = null!;

    private DataGridView _guestsGrid = null!;
    private TextBox _guestLastNameText = null!;
    private TextBox _guestFirstNameText = null!;
    private TextBox _guestMiddleNameText = null!;
    private DateTimePicker _guestBirthDatePicker = null!;
    private TextBox _guestPassportText = null!;
    private TextBox _guestPhoneText = null!;
    private TextBox _guestEmailText = null!;
    private TextBox _guestAddressText = null!;

    private DataGridView _availableRoomsGrid = null!;
    private DataGridView _bookingsGrid = null!;
    private DateTimePicker _availableFromPicker = null!;
    private DateTimePicker _availableToPicker = null!;
    private ComboBox _availableTypeCombo = null!;
    private NumericUpDown _availableCapacityNumeric = null!;
    private ComboBox _bookingGuestCombo = null!;
    private ComboBox _bookingRoomCombo = null!;
    private DateTimePicker _bookingFromPicker = null!;
    private DateTimePicker _bookingToPicker = null!;
    private ComboBox _bookingStatusCombo = null!;
    private TextBox _bookingCommentText = null!;

    private DataGridView _pendingBookingsGrid = null!;
    private DataGridView _staysGrid = null!;
    private DataGridView _archiveGrid = null!;
    private ComboBox _stayGuestCombo = null!;
    private ComboBox _stayRoomCombo = null!;
    private DateTimePicker _stayCheckInPicker = null!;
    private DateTimePicker _stayPlannedOutPicker = null!;
    private TextBox _stayNotesText = null!;

    private DataGridView _servicesGrid = null!;
    private DataGridView _ordersGrid = null!;
    private TextBox _serviceNameText = null!;
    private NumericUpDown _servicePriceNumeric = null!;
    private CheckBox _serviceIsActiveCheck = null!;
    private ComboBox _orderStayCombo = null!;
    private ComboBox _orderServiceCombo = null!;
    private NumericUpDown _orderQuantityNumeric = null!;

    private DataGridView _paymentStaysGrid = null!;
    private DataGridView _paymentsGrid = null!;
    private ComboBox _paymentPayerCombo = null!;
    private ComboBox _paymentMethodCombo = null!;
    private Label _paymentTotalLabel = null!;

    public MainForm()
    {
        Text = $"{HotelBranding.SystemTitle} - {HotelBranding.HotelName}";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1180, 760);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = HotelBranding.Background;

        try
        {
            Icon = HotelBranding.CreateAppIcon();
        }
        catch
        {
            // If Windows rejects a runtime icon handle, the default app icon is acceptable.
        }

        BuildInterface();

        Load += (_, _) =>
        {
            RefreshAll();
            FindAvailableRooms();
        };
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = HotelBranding.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _tabs.Dock = DockStyle.Fill;
        _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _tabs.ItemSize = new Size(180, 34);
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.Padding = new Point(14, 4);
        _tabs.DrawItem += DrawTab;
        _tabs.TabPages.Add(BuildDashboardTab());
        _tabs.TabPages.Add(BuildRoomsTab());
        _tabs.TabPages.Add(BuildGuestsTab());
        _tabs.TabPages.Add(BuildBookingsTab());
        _tabs.TabPages.Add(BuildStaysTab());
        _tabs.TabPages.Add(BuildServicesTab());
        _tabs.TabPages.Add(BuildPaymentsTab());

        _databaseStatus.Text = $"{HotelBranding.HotelName} | База данных: {Database.DatabasePath}";
        _databaseStatus.ForeColor = HotelBranding.MutedInk;
        _statusStrip.BackColor = HotelBranding.SurfaceAlt;
        _statusStrip.SizingGrip = false;
        _statusStrip.Items.Add(_databaseStatus);

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(_tabs, 0, 1);

        Controls.Add(root);
        Controls.Add(_statusStrip);
    }

    private TabPage BuildDashboardTab()
    {
        var page = CreateTabPage("Сводка");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = HotelBranding.Background,
            RowCount = 3,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hero = new GradientHeaderPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 18, 26, 18)
        };
        var logo = new PictureBox
        {
            Image = HotelBranding.CreateLogoBitmap(104),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(104, 104),
            Location = new Point(24, 30)
        };
        var title = new Label
        {
            Text = HotelBranding.HotelName,
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 26F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(150, 30)
        };
        var subtitle = new Label
        {
            Text = HotelBranding.Tagline,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(231, 238, 232),
            Font = new Font("Segoe UI", 12F, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(154, 78)
        };
        _statsLabel = new Label
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            Size = new Size(560, 112),
            Location = new Point(560, 30)
        };
        hero.Resize += (_, _) => _statsLabel.Location = new Point(Math.Max(420, hero.Width - 590), 34);
        hero.Controls.Add(logo);
        hero.Controls.Add(title);
        hero.Controls.Add(subtitle);
        hero.Controls.Add(_statsLabel);

        var header = CreateSectionLabel("Последние действия в базе данных");
        _auditGrid = CreateGrid();

        layout.Controls.Add(hero, 0, 0);
        layout.Controls.Add(header, 0, 1);
        layout.Controls.Add(_auditGrid, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private Control CreateHeader()
    {
        var header = new GradientHeaderPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 12, 22, 12)
        };

        var logo = new PictureBox
        {
            Image = HotelBranding.CreateLogoBitmap(72),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(72, 72),
            Location = new Point(24, 13)
        };
        var title = new Label
        {
            Text = $"{HotelBranding.HotelName}  |  {HotelBranding.SystemTitle}",
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(114, 18)
        };
        var subtitle = new Label
        {
            Text = "Управление гостиницей: номера, гости, бронирование, заселение, услуги, оплата и архив",
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(230, 238, 232),
            Font = new Font("Segoe UI", 11F, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(118, 58)
        };
        var today = new Label
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(230, 30),
            Location = new Point(Width - 275, 34)
        };
        header.Resize += (_, _) => today.Location = new Point(header.Width - 260, 34);

        header.Controls.Add(logo);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(today);
        return header;
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        var selected = e.Index == _tabs.SelectedIndex;
        var bounds = e.Bounds;
        using var background = new SolidBrush(selected ? HotelBranding.Primary : HotelBranding.SurfaceAlt);
        using var border = new Pen(HotelBranding.Border);
        using var textBrush = new SolidBrush(selected ? Color.White : HotelBranding.Ink);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        e.Graphics.FillRectangle(background, bounds);
        e.Graphics.DrawRectangle(border, bounds);
        e.Graphics.DrawString(_tabs.TabPages[e.Index].Text, Font, textBrush, bounds, format);
    }

    private void RefreshAll()
    {
        RunSafely(() =>
        {
            _loading = true;
            RefreshLookups();
            RefreshRooms();
            RefreshGuests();
            RefreshBookings();
            RefreshStays();
            RefreshServices();
            RefreshOrders();
            RefreshPayments();
            RefreshDashboard();
        });

        _loading = false;
    }

    private void RefreshDashboard()
    {
        var rooms = Database.Scalar<long>("SELECT COUNT(*) FROM Rooms");
        var guests = Database.Scalar<long>("SELECT COUNT(*) FROM Guests");
        var bookings = Database.Scalar<long>("SELECT COUNT(*) FROM Bookings WHERE Status IN ('Новое', 'Подтверждено')");
        var activeStays = Database.Scalar<long>("SELECT COUNT(*) FROM Stays WHERE Status = 'Проживает'");
        var archive = Database.Scalar<long>("SELECT COUNT(*) FROM GuestArchive");
        var totalRecords = Database.Scalar<long>(
            """
            SELECT
                (SELECT COUNT(*) FROM Rooms) +
                (SELECT COUNT(*) FROM Guests) +
                (SELECT COUNT(*) FROM Bookings) +
                (SELECT COUNT(*) FROM Stays) +
                (SELECT COUNT(*) FROM Services) +
                (SELECT COUNT(*) FROM ServiceOrders) +
                (SELECT COUNT(*) FROM Payments)
            """);

        _statsLabel.Text =
            $"Номеров: {rooms}    Гостей: {guests}\n" +
            $"Активных броней: {bookings}    Проживают: {activeStays}\n" +
            $"Архив выездов: {archive}    Всего записей: {totalRecords}";

        SetGrid(
            _auditGrid,
            Database.Query(
                """
                SELECT Id, CreatedAt AS 'Дата', Action AS 'Действие', Entity AS 'Таблица',
                       EntityId AS 'Код', Details AS 'Описание'
                FROM AuditLog
                ORDER BY Id DESC
                LIMIT 40;
                """));
    }

    private void RefreshLookups()
    {
        BindCombo(_roomTypeCombo, Database.Query("SELECT Id, Name FROM RoomTypes ORDER BY Name;"));
        BindCombo(_availableTypeCombo, Database.Query(
            """
            SELECT Id, Name
            FROM (
                SELECT 0 AS Id, 'Все типы' AS Name, 0 AS SortOrder
                UNION ALL
                SELECT Id, Name, 1 AS SortOrder FROM RoomTypes
            )
            ORDER BY SortOrder, Name;
            """));
        BindCombo(_bookingGuestCombo, GuestsLookup());
        BindCombo(_stayGuestCombo, GuestsLookup());
        BindCombo(_paymentPayerCombo, GuestsLookup());
        BindCombo(_bookingRoomCombo, RoomsLookup());
        BindCombo(_stayRoomCombo, RoomsLookup());
        BindCombo(_orderStayCombo, ActiveStaysLookup());
        BindCombo(_orderServiceCombo, Database.Query("SELECT Id, Name FROM Services WHERE IsActive = 1 ORDER BY Name;"));
    }

    private static DataTable GuestsLookup()
    {
        return Database.Query(
            """
            SELECT Id, LastName || ' ' || FirstName || ' ' || IFNULL(MiddleName, '') AS Name
            FROM Guests
            ORDER BY LastName, FirstName;
            """);
    }

    private static DataTable RoomsLookup()
    {
        return Database.Query(
            """
            SELECT r.Id, r.Number || ' - ' || rt.Name || ', ' || r.DailyPrice || ' руб./сут.' AS Name
            FROM Rooms r
            JOIN RoomTypes rt ON rt.Id = r.RoomTypeId
            WHERE r.Status <> 'Ремонт'
            ORDER BY r.Number;
            """);
    }

    private static DataTable ActiveStaysLookup()
    {
        return Database.Query(
            """
            SELECT s.Id,
                   '№' || r.Number || ' - ' || g.LastName || ' ' || g.FirstName AS Name
            FROM Stays s
            JOIN Rooms r ON r.Id = s.RoomId
            JOIN Guests g ON g.Id = s.GuestId
            WHERE s.Status = 'Проживает'
            ORDER BY r.Number;
            """);
    }

    private static TabPage CreateTabPage(string title)
    {
        return new TabPage(title)
        {
            Padding = new Padding(10),
            BackColor = HotelBranding.Background
        };
    }

    private static DataGridView CreateGrid(bool multiSelect = false)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = HotelBranding.Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = HotelBranding.Border,
            MultiSelect = multiSelect,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            RowTemplate = { Height = 34 }
        };
        StyleGrid(grid);
        return grid;
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = HotelBranding.PrimaryDark,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
            Padding = new Padding(4, 0, 0, 0)
        };
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(120, 36),
            Height = 36,
            Margin = new Padding(4),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };

        var lower = text.ToLowerInvariant();
        if (lower.Contains("удал") || lower.Contains("отмен"))
        {
            button.BackColor = HotelBranding.Danger;
            button.ForeColor = Color.White;
        }
        else if (lower.Contains("очист"))
        {
            button.BackColor = HotelBranding.SurfaceAlt;
            button.ForeColor = HotelBranding.Ink;
        }
        else if (lower.Contains("оплат") || lower.Contains("засел") || lower.Contains("сохран") || lower.Contains("создать"))
        {
            button.BackColor = HotelBranding.Primary;
            button.ForeColor = Color.White;
        }
        else
        {
            button.BackColor = HotelBranding.Accent;
            button.ForeColor = Color.White;
        }

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(button.BackColor);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(button.BackColor);
        return button;
    }

    private static TableLayoutPanel CreateEditorLayout(int rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(10),
            BackColor = HotelBranding.Surface,
            ColumnCount = 2,
            RowCount = rows
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < rows; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        return layout;
    }

    private static TableLayoutPanel CreateMasterEditorLayout(int editorWidth = 440)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = HotelBranding.Background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, editorWidth));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    private static void AddField(TableLayoutPanel layout, string labelText, Control control, int row)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 8, 8),
            ForeColor = HotelBranding.MutedInk,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
        };

        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(0, 4, 0, 4);

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static void SetEditorSplitter(SplitContainer split, int editorWidth)
    {
        if (split.Width <= editorWidth + 260)
        {
            return;
        }

        var target = split.Width - editorWidth;
        var maximum = split.Width - split.SplitterWidth - 120;
        split.SplitterDistance = Math.Min(Math.Max(360, target), maximum);
    }

    private static FlowLayoutPanel ButtonsPanel(params Control[] buttons)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        panel.Controls.AddRange(buttons);
        return panel;
    }

    private static TextBox TextBoxSingle()
    {
        return new TextBox { Width = 240 };
    }

    private static TextBox TextBoxMulti()
    {
        return new TextBox
        {
            Multiline = true,
            Height = 70,
            ScrollBars = ScrollBars.Vertical,
            Width = 260
        };
    }

    private static ComboBox Combo()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 240
        };
    }

    private static NumericUpDown MoneyNumeric()
    {
        return new NumericUpDown
        {
            DecimalPlaces = 2,
            Maximum = 1000000,
            Minimum = 0,
            Increment = 100,
            Width = 160
        };
    }

    private static NumericUpDown CountNumeric(int maximum = 100)
    {
        return new NumericUpDown
        {
            Minimum = 1,
            Maximum = maximum,
            Width = 120
        };
    }

    private static DateTimePicker DatePicker(DateTime? value = null)
    {
        return new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = value ?? DateTime.Today,
            Width = 150
        };
    }

    private static void SetGrid(DataGridView grid, DataTable table)
    {
        grid.DataSource = table;
        StyleGrid(grid);

        if (grid.Columns.Contains("Id"))
        {
            grid.Columns["Id"]!.Visible = false;
        }
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.ColumnHeadersDefaultCellStyle.BackColor = HotelBranding.GridHeader;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersHeight = 40;
        grid.DefaultCellStyle.BackColor = HotelBranding.Surface;
        grid.DefaultCellStyle.ForeColor = HotelBranding.Ink;
        grid.DefaultCellStyle.SelectionBackColor = HotelBranding.GridSelection;
        grid.DefaultCellStyle.SelectionForeColor = HotelBranding.Ink;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 247);
        grid.RowsDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
    }

    private static void BindCombo(ComboBox combo, DataTable table)
    {
        var selectedValue = combo.SelectedValue;
        combo.DisplayMember = "Name";
        combo.ValueMember = "Id";
        combo.DataSource = table;

        if (selectedValue is not null)
        {
            try
            {
                combo.SelectedValue = selectedValue;
            }
            catch
            {
                combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
            }
        }
    }

    private static int ComboId(ComboBox combo)
    {
        if (combo.SelectedValue is null)
        {
            return 0;
        }

        return Convert.ToInt32(combo.SelectedValue, CultureInfo.InvariantCulture);
    }

    private static int SelectedId(DataGridView grid)
    {
        if (grid.CurrentRow?.Cells["Id"].Value is null)
        {
            return 0;
        }

        return Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
    }

    private static string DateValue(DateTimePicker picker)
    {
        return picker.Value.Date.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    private static DataRow? FirstRow(DataTable table)
    {
        return table.Rows.Count == 0 ? null : table.Rows[0];
    }

    private static void SelectComboValue(ComboBox combo, object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return;
        }

        combo.SelectedValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static DateTime ParseDate(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return DateTime.Today;
        }

        return DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
    }

    private void RunSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                HotelBranding.SystemTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
