using System.Data;
using System.Globalization;
using HotelAdminArm.Data;
using HotelAdminArm.Domain;

namespace HotelAdminArm;

public sealed partial class MainForm : Form
{
    private const string DateFormat = "yyyy-MM-dd";
    private bool _loading;
    internal Action<Exception>? ErrorHandler {get;set;}
    private int _roomEditId, _guestEditId, _bookingEditId, _serviceEditId;
    private CheckBox _roomActiveCheck = null!, _guestActiveCheck = null!;
    private Label _clockLabel = null!;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 30000 };
    private DateTime _lastDay = Database.Today;
    private byte[]? _selectedRoomPhoto;

    private readonly TabControl _tabs = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _databaseStatus = new();

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
    private OperationalDatePicker _availableFromPicker = null!;
    private OperationalDatePicker _availableToPicker = null!;
    private ComboBox _availableTypeCombo = null!;
    private NumericUpDown _availableCapacityNumeric = null!;
    private ComboBox _bookingGuestCombo = null!;
    private ComboBox _bookingRoomCombo = null!;
    private OperationalDatePicker _bookingFromPicker = null!;
    private OperationalDatePicker _bookingToPicker = null!;
    private ComboBox _bookingStatusCombo = null!;
    private TextBox _bookingCommentText = null!;

    private DataGridView _pendingBookingsGrid = null!;
    private DataGridView _staysGrid = null!;
    private DataGridView _archiveGrid = null!;
    private ComboBox _stayGuestCombo = null!;
    private ComboBox _stayRoomCombo = null!;
    private DateTimePicker _stayCheckInPicker = null!;
    private OperationalDatePicker _stayPlannedOutPicker = null!;
    private TextBox _stayNotesText = null!;

    private DataGridView _servicesGrid = null!;
    private DataGridView _ordersGrid = null!;
    private TextBox _serviceNameText = null!;
    private NumericUpDown _servicePriceNumeric = null!;
    private CheckBox _serviceIsActiveCheck = null!;
    private ComboBox _orderStayCombo = null!;
    private ComboBox _orderServiceCombo = null!;
    private NumericUpDown _orderQuantityNumeric = null!;
    private DateTimePicker _orderDatePicker = null!;
    private ComboBox _serviceMealCombo = null!, _serviceRuleCombo = null!, _orderGuestCombo = null!;
    private Label _orderRuleLabel = null!;

    private DataGridView _paymentStaysGrid = null!;
    private DataGridView _paymentsGrid = null!;
    private Label _paymentTotalLabel = null!;

    public MainForm()
    {
        Text = $"{HotelBranding.SystemTitle} - {HotelBranding.HotelName}";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
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
        Shown+=(_,_)=>FitToWorkingArea();
        DpiChanged+=(_,_)=>BeginInvoke(new Action(FitToWorkingArea));
        _refreshTimer.Tick += (_, _) => {
            _clockLabel.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            if (_lastDay != Database.Today) { _lastDay=Database.Today; RefreshAll(false); }
            else if (!_loading) RunSafely(UpdateOrderRule);
        };
        _refreshTimer.Start();
        // Rebinding every lookup on activation closes an open native dropdown and
        // steals input focus. Operations refresh their own data; external edits can
        // be loaded with «Обновить всё». Only a day rollover needs an automatic reload.
        Activated += (_, _) => { if (!_loading && _roomsGrid.DataSource is not null && _lastDay!=Database.Today) BeginInvoke(new Action(() => { if(!IsDisposed && !_loading){_lastDay=Database.Today;RefreshAll(false);} })); };
        FormClosed += (_, _) => { _refreshTimer.Dispose(); _roomPhotoBox.Image?.Dispose(); };

        Load += (_, _) =>
        {
            RefreshAll();
            ClearRoomForm(); ClearGuestForm(); ClearBookingForm(); ClearServiceForm();
        };
    }

    private void FitToWorkingArea()
    {
        if(IsDisposed)return;
        var area=Screen.FromControl(this).WorkingArea;
        MinimumSize=new Size(Math.Min(1000,area.Width),Math.Min(700,area.Height));
        if(WindowState==FormWindowState.Normal)
            Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));

        _tabs.Dock = DockStyle.Fill;
        _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _tabs.ItemSize = new Size(145, 34);
        _tabs.Multiline = false;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.Padding = new Point(14, 4);
        _tabs.DrawItem += DrawTab;
        _tabs.SelectedIndexChanged+=(_,_)=>{if(!_loading && _calendarGrid is not null && _tabs.SelectedTab?.Text=="Календарь")RunSafely(RefreshCalendar);};
        _tabs.TabPages.Add(BuildDashboardTab());
        _tabs.TabPages.Add(BuildRoomsTab());
        _tabs.TabPages.Add(BuildGuestsTab());
        _tabs.TabPages.Add(BuildBookingsTab());
        _tabs.TabPages.Add(BuildStaysTab());
        _tabs.TabPages.Add(BuildServicesTab());
        _tabs.TabPages.Add(BuildPaymentsTab());
        _tabs.TabPages.Add(BuildCalendarTab());
        _tabs.TabPages.Add(BuildOperationsTab());
        AddSectionHelp();
        _tabs.FontChanged+=(_,_)=>UpdateTabMetrics();
        UpdateTabMetrics();

        _databaseStatus.Text = $"{HotelBranding.HotelName} | База данных: {Database.DatabasePath}";
        _databaseStatus.ForeColor = HotelBranding.MutedInk;
        _statusStrip.BackColor = HotelBranding.SurfaceAlt;
        _statusStrip.SizingGrip = false;
        _statusStrip.Items.Add(_databaseStatus);

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(_tabs, 0, 1);

        Controls.Add(root);
        Controls.Add(_statusStrip);
        ConstrainSingleColumns(root);
    }

    private static void ConstrainSingleColumns(Control parent)
    {
        if(parent is TableLayoutPanel table && table.ColumnCount<=1 && table.ColumnStyles.Count==0)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        if(parent is TableLayoutPanel editor && Equals(editor.Tag,"Editor"))
        {
            var last=editor.RowCount-1;
            var buttons=editor.Controls.Cast<Control>().OfType<WrappingButtonPanel>().FirstOrDefault(c=>editor.GetRow(c)==last);
            if(buttons is not null)
            {
                foreach(var blank in editor.Controls.Cast<Control>().OfType<Label>().Where(c=>editor.GetRow(c)==last && c.Text.Length==0).ToArray()){editor.Controls.Remove(blank);blank.Dispose();}
                editor.SetCellPosition(buttons,new TableLayoutPanelCellPosition(0,last));editor.SetColumnSpan(buttons,2);
            }
        }
        foreach(Control child in parent.Controls)ConstrainSingleColumns(child);
    }

    private void UpdateTabMetrics()
    {
        if(_tabs.TabCount==0)return;
        var width=_tabs.TabPages.Cast<TabPage>().Max(p=>TextRenderer.MeasureText(p.Text,_tabs.Font).Width)+24;
        _tabs.ItemSize=new Size(Math.Max(145,width),Math.Max(34,_tabs.Font.Height+14));
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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));

        var cards=new WrappingButtonPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true};
        var names=new[]{"Номера","Гости","Активные брони","Проживают","Архив выездов","Всего записей"};
        for(var i=0;i<names.Length;i++)
        {
            var card=new TableLayoutPanel{Width=180,Height=64,MinimumSize=new Size(170,64),ColumnCount=1,RowCount=2,BackColor=HotelBranding.Surface,Padding=new Padding(8),Margin=new Padding(0,0,8,8)};
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            card.RowStyles.Add(new RowStyle(SizeType.Percent,45));card.RowStyles.Add(new RowStyle(SizeType.Percent,55));
            card.Controls.Add(new Label{Text=names[i],Dock=DockStyle.Fill,ForeColor=HotelBranding.MutedInk},0,0);
            _statValues[i]=new Label{Text="0",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,16,FontStyle.Bold),ForeColor=HotelBranding.PrimaryDark};
            card.Controls.Add(_statValues[i],0,1);cards.Controls.Add(card);
        }

        var header = CreateSectionLabel("Последние действия в базе данных");
        _auditGrid = CreateGrid();

        layout.Controls.Add(cards, 0, 0);
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

        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,BackColor=Color.Transparent,ColumnCount=3,RowCount=1};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,82));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,185));
        layout.Controls.Add(new PictureBox {Image=HotelBranding.CreateLogoBitmap(72),Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.Transparent},0,0);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(new Label {Text=$"{HotelBranding.HotelName} | {HotelBranding.SystemTitle}",Dock=DockStyle.Fill,BackColor=Color.Transparent,ForeColor=Color.White,Font=new Font("Segoe UI",14F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true},1,0);
        _clockLabel=new Label {Text=DateTime.Now.ToString("dd.MM.yyyy HH:mm"),Dock=DockStyle.Fill,BackColor=Color.Transparent,ForeColor=Color.White,Font=new Font("Segoe UI",10F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleRight};
        layout.Controls.Add(_clockLabel,2,0);header.Controls.Add(layout);
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

    private void RefreshAll(bool reloadEditors = true)
    {
        RunSafely(() =>
        {
            _loading = true;
            try {
                SynchronizeOperationalDates();
                RefreshLookups(); RefreshRooms(); RefreshGuests(); RefreshBookings(); RefreshStays();
                RefreshServices(); RefreshOrders(); RefreshPayments(); RefreshDashboard(); RefreshExtras();
                if (_availableFromPicker.Value.Date >= Database.Today && _availableToPicker.Value.Date > _availableFromPicker.Value.Date)
                    SetGrid(_availableRoomsGrid, HotelService.Available(_availableFromPicker.Value,_availableToPicker.Value,ComboId(_availableTypeCombo),(int)_availableCapacityNumeric.Value));
            } finally { _loading = false; }
            if(_roomEditId>0 && SelectedId(_roomsGrid)==0)ClearRoomForm();
            if(_guestEditId>0 && SelectedId(_guestsGrid)==0)ClearGuestForm();
            if(_bookingEditId>0 && SelectedId(_bookingsGrid)==0)ClearBookingForm();
            if(_serviceEditId>0 && SelectedId(_servicesGrid)==0)ClearServiceForm();
            if (reloadEditors) {
                if (_roomEditId>0 && SelectedId(_roomsGrid)==_roomEditId) LoadSelectedRoom();
                if (_guestEditId>0 && SelectedId(_guestsGrid)==_guestEditId) LoadSelectedGuest();
                if (_bookingEditId>0 && SelectedId(_bookingsGrid)==_bookingEditId) LoadSelectedBooking();
                if (_serviceEditId>0 && SelectedId(_servicesGrid)==_serviceEditId) LoadSelectedService();
            }
            CalculateSelectedPayment();
            RefreshOrderGuests();
            UpdateOrderRule();
        });
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

        var counts=new[]{rooms,guests,bookings,activeStays,archive,totalRecords};
        for(var i=0;i<counts.Length;i++)_statValues[i].Text=counts[i].ToString();

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
        if(_availableTypeCombo.SelectedIndex<0)_availableTypeCombo.SelectedValue=0;
        BindCombo(_bookingGuestCombo, GuestsLookup());
        BindCombo(_stayGuestCombo, GuestsLookup());
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
            FROM Guests WHERE IsActive=1
            ORDER BY LastName, FirstName;
            """);
    }

    private static DataTable RoomsLookup()
    {
        return Database.Query("SELECT Id,Number || CASE WHEN IsActive=0 THEN ' (неактивен)' WHEN UnderMaintenance=1 THEN ' (ремонт)' ELSE '' END AS Name FROM Rooms ORDER BY Number");
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

    private void AddSectionHelp()
    {
        var descriptions=new[]{
            "Сводка: показатели гостиницы и последние действия администратора.",
            "Номера: тип, вместимость, тариф за весь номер в сутки, фотографии и доступность.",
            "Гости: регистрационные данные и поиск. Отключение сохраняет историю гостя.",
            "Бронирование: найдите свободный номер на выбранные даты и оформите бронь.",
            "Заселение и архив: заезд, проживающие, продление, переселение и история выездов.",
            "Услуги: каталог и заказы. Питание — одна порция каждого вида на гостя в день.",
            "Оплата: выберите одно или несколько проживаний, проверьте счёт и подтвердите выезд.",
            "Календарь: доступность номеров на 30 дней. Двойной щелчок по свободной дате — новая бронь.",
            "Контроль и копии: сегодняшние операции, проблемы истории, резервное копирование и восстановление."};
        for(var i=0;i<_tabs.TabCount;i++)
        {
            var page=_tabs.TabPages[i];var content=page.Controls[0];page.Controls.Remove(content);
            var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            shell.Controls.Add(new Label{Text=descriptions[i],AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(4,3,4,7),ForeColor=HotelBranding.MutedInk},0,0);
            shell.Controls.Add(content,0,1);page.Controls.Add(shell);
        }
    }

    private static TabPage CreateTabPage(string title)
    {
        return new TabPage(title)
        {
            Padding = new Padding(10),
            BackColor = HotelBranding.Background
        };
    }

    private DataGridView CreateGrid(bool multiSelect = false)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
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
        grid.FontChanged+=(_,_)=>FitGridColumns(grid);
        grid.DataError+=(_,e)=>{e.ThrowException=false;RunSafely(()=>throw new InvalidOperationException("Не удалось показать значение в таблице. Обновите список.",e.Exception));};
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
            MinimumSize = new Size(96, 36),
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
            Tag = "Editor",
            Padding = new Padding(10),
            BackColor = HotelBranding.Surface,
            ColumnCount = 2,
            RowCount = rows
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.SizeChanged+=(_,_)=>{
            var target=Math.Min(150*layout.Font.Size/10f,Math.Max(90,layout.ClientSize.Width*0.36f));
            if(Math.Abs(layout.ColumnStyles[0].Width-target)>1)layout.ColumnStyles[0].Width=target;
        };

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
        var panel = new WrappingButtonPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
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
            Value = value ?? Database.Today,
            Width = 150
        };
    }

    private void SynchronizeOperationalDates()
    {
        foreach (var picker in new[] { _availableFromPicker, _availableToPicker,
            _bookingFromPicker, _bookingToPicker, _stayPlannedOutPicker, _calendarFrom })
            picker.SynchronizeDateBounds();
        _clockLabel.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
    }

    private static void SetGrid(DataGridView grid, DataTable table)
    {
        var current=SelectedId(grid);
        var selected=grid.SelectedRows.Cast<DataGridViewRow>().Where(r=>r.Cells["Id"].Value is not null).Select(r=>Convert.ToInt32(r.Cells["Id"].Value)).ToHashSet();
        if(grid.Columns.Count!=table.Columns.Count || table.Columns.Cast<DataColumn>().Any(c=>!grid.Columns.Contains(c.ColumnName))) {
            grid.DataSource=null;grid.Columns.Clear();grid.AutoGenerateColumns=false;
            foreach(DataColumn column in table.Columns)grid.Columns.Add(new DataGridViewTextBoxColumn {Name=column.ColumnName,HeaderText=column.ColumnName,DataPropertyName=column.ColumnName,Visible=column.ColumnName!="Id"});
        }
        grid.DataSource=table;
        FitGridColumns(grid);
        if(grid.Columns.Contains("Id"))grid.Columns["Id"]!.Visible=false;
        grid.ClearSelection(); grid.CurrentCell=null;
        foreach(DataGridViewRow row in grid.Rows) {
            if(row.Cells["Id"].Value is null) continue;
            var id=Convert.ToInt32(row.Cells["Id"].Value);
            if(id==current) grid.CurrentCell=row.Cells.Cast<DataGridViewCell>().First(cell=>cell.Visible);
        }
        grid.ClearSelection();
        foreach(DataGridViewRow row in grid.Rows)if(selected.Contains(Convert.ToInt32(row.Cells["Id"].Value)))row.Selected=true;
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.ColumnHeadersDefaultCellStyle.BackColor = HotelBranding.GridHeader;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = SystemFonts.MessageBoxFont;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersHeight = 40;
        grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
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
                combo.SelectedIndex = -1;
            }
        }
        else combo.SelectedIndex=-1;
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
        if (grid.CurrentRow is null || !grid.Columns.Contains("Id") || grid.CurrentRow.Cells["Id"].Value is null)
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
            return Database.Today;
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
            if(ErrorHandler is not null){ErrorHandler(ex);return;}
            MessageBox.Show(
                FriendlyError(ex),
                HotelBranding.SystemTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
