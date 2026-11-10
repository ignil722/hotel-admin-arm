using System.Data;
using HotelAdminArm.Data;
using HotelAdminArm.Domain;

namespace HotelAdminArm;

public sealed partial class MainForm
{
    private TabPage BuildBookingsTab()
    {
        var page = CreateTabPage("Бронирование");
        _availableFromPicker = new OperationalDatePicker(Database.Today);
        _availableToPicker = new OperationalDatePicker(Database.Today.AddDays(1), minimumDays: 1);
        _availableTypeCombo = Combo();
        _availableCapacityNumeric = CountNumeric(10);
        var findButton = CreateButton("Найти свободные номера");
        findButton.Click += (_, _) => FindAvailableRooms();

        findButton.Margin=new Padding(4,24,4,4);
        var searchPanel=ButtonsPanel(FilterBox("Дата заезда",_availableFromPicker),FilterBox("Дата выезда",_availableToPicker),FilterBox("Тип",_availableTypeCombo,200),FilterBox("Минимум мест",_availableCapacityNumeric,130),findButton);

        _availableRoomsGrid = CreateGrid();
        _availableRoomsGrid.DoubleClick += (_, _) => UseAvailableRoomForBooking();

        _bookingsGrid = CreateGrid();
        _bookingsGrid.CurrentCellChanged += (_, _) =>
        {
            // Windows selects the first row when a hidden tab becomes visible.
            // Only a user's selection should populate the new-booking editor.
            if (_bookingsGrid.ContainsFocus || SelectedId(_bookingsGrid)==0)
                RunSafely(LoadSelectedBooking);
        };
        _bookingsGrid.CellClick += (_, e) => { if(e.RowIndex>=0)RunSafely(LoadSelectedBooking); };

        // Compact filters use their preferred height; results take the remaining space.
        searchPanel.Dock=DockStyle.Top;searchPanel.AutoSize=true;
        var results=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));results.RowStyles.Add(new RowStyle(SizeType.AutoSize));results.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        results.Controls.Add(searchPanel,0,0);results.Controls.Add(_availableRoomsGrid,0,1);
        _bookingSections=Sections(("Брони",Workspace(SearchableGrid(_bookingsGrid),CreateBookingEditor())),("Поиск свободных номеров",results));
        page.Controls.Add(_bookingSections);
        return page;
    }

    private TabPage BuildStaysTab()
    {
        var page = CreateTabPage("Заселение и архив");
        _pendingBookingsGrid = CreateGrid();
        _staysGrid = CreateGrid();
        _archiveGrid = CreateGrid();

        var checkInButton = CreateButton("Заселить выбранную бронь");
        checkInButton.Click += (_, _) => CheckInSelectedBooking();
        var checkOutButton = CreateButton("Выезд");
        checkOutButton.Click += (_, _) => CheckoutSingleStay();
        var extend=CreateButton("Продлить"); extend.Click+=(_,_)=>ExtendSelectedStay();
        var move=CreateButton("Переселить"); move.Click+=(_,_)=>MoveSelectedStay();
        var occupants=CreateButton("Проживающие"); occupants.Click+=(_,_)=>ManageOccupants();
        var card=CreateButton("Карточка");card.Click+=(_,_)=>RunSafely(()=>ShowText(HotelService.RegistrationCard(SelectedId(_staysGrid)),true,"Карточки регистрации гостей"));
        var bill=CreateButton("Счёт из архива");bill.Click+=(_,_)=>RunSafely(()=>{var id=SelectedId(_archiveGrid);if(id==0)throw new InvalidOperationException("Выберите выезд.");ShowText(HotelService.ClosedBill(id),true);});
        _staySections=Sections(
            ("Проживания",Workspace(WithActions(SearchableGrid(_staysGrid),checkOutButton,extend,move,occupants,card),CreateStayEditor())),
            ("Заезд по брони",WithActions(SearchableGrid(_pendingBookingsGrid),checkInButton)),
            ("Архив",WithActions(SearchableGrid(_archiveGrid),bill)));
        page.Controls.Add(_staySections);
        return page;
    }

    private TableLayoutPanel CreateBookingEditor()
    {
        _bookingGuestCombo = Combo();
        _bookingRoomCombo = Combo();
        _bookingFromPicker = new OperationalDatePicker(allowEmpty: true);
        _bookingToPicker = new OperationalDatePicker(allowEmpty: true);
        _bookingStatusCombo = Combo();
        _bookingStatusCombo.DataSource = new[] { "Новое", "Подтверждено" };
        _bookingCommentText = TextBoxMulti();

        var addButton = CreateButton("Создать бронь");
        addButton.Click += (_, _) => SaveBooking(false);
        var updateButton = CreateButton("Сохранить");
        updateButton.Click += (_, _) => SaveBooking(true);
        var cancelButton = CreateButton("Отменить");
        cancelButton.Click += (_, _) => CancelBooking();
        var deleteButton = CreateButton("Удалить");
        deleteButton.Click += (_, _) => DeleteBooking();

        var newButton=CreateButton("Новая бронь"); newButton.Click+=(_,_)=>ClearBookingForm();
        var confirmButton=CreateButton("Подтвердить"); confirmButton.Click+=(_,_)=>RunSafely(()=>{_bookingStatusCombo.SelectedItem="Подтверждено";SaveBooking(true);});
        var editor = CreateEditorLayout(7);
        AddField(editor, "Гость", _bookingGuestCombo, 0);
        AddField(editor, "Номер", _bookingRoomCombo, 1);
        AddField(editor, "Заезд", _bookingFromPicker, 2);
        AddField(editor, "Выезд", _bookingToPicker, 3);
        AddField(editor, "Статус", _bookingStatusCombo, 4);
        AddField(editor, "Комментарий", _bookingCommentText, 5);
        editor.Controls.Add(new Label(), 0, 6);
        editor.Controls.Add(ButtonsPanel(newButton,addButton,updateButton,confirmButton,cancelButton,deleteButton), 1, 6);
        return editor;
    }

    private TableLayoutPanel CreateStayEditor()
    {
        _stayGuestCombo = Combo();
        _stayRoomCombo = Combo();
        _stayCheckInPicker = DatePicker(Database.Today);
        _stayCheckInPicker.Enabled=false;
        _stayPlannedOutPicker = new OperationalDatePicker(Database.Today.AddDays(1), minimumDays: 1);
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
        SetGrid(_bookingsGrid,Database.Query("""
            SELECT b.Id,g.LastName||' '||g.FirstName AS 'Гость',r.Number AS 'Номер',b.DateFrom AS 'Заезд',b.DateTo AS 'Выезд',b.Status AS 'Статус',
                b.RateKopecks/100.0 AS 'Тариф',CASE WHEN b.Status IN('Новое','Подтверждено') AND b.DateTo<=hotel_today() THEN 'Период истёк' ELSE '' END AS 'Внимание',b.Comment AS 'Комментарий'
            FROM Bookings b JOIN Guests g ON g.Id=b.GuestId JOIN Rooms r ON r.Id=b.RoomId ORDER BY b.DateFrom DESC,b.Id DESC
            """));
        SetGrid(_pendingBookingsGrid,Database.Query("""
            SELECT b.Id,g.LastName||' '||g.FirstName AS 'Гость',r.Number AS 'Номер',b.DateFrom AS 'Заезд',b.DateTo AS 'Выезд'
            FROM Bookings b JOIN Guests g ON g.Id=b.GuestId JOIN Rooms r ON r.Id=b.RoomId
            WHERE b.Status='Подтверждено' AND b.DateFrom<=hotel_today() AND b.DateTo>hotel_today() ORDER BY b.DateFrom
            """));
    }
    private void RefreshStays()
    {
        SetGrid(_staysGrid,Database.Query("""
            SELECT s.Id,g.LastName||' '||g.FirstName AS 'Гость',r.Number AS 'Номер',s.CheckInDate AS 'Заезд',s.PlannedCheckOutDate AS 'Плановый выезд',
                CASE WHEN s.PlannedCheckOutDate<=hotel_today() THEN 'Выезд просрочен' ELSE '' END AS 'Внимание',s.Notes AS 'Примечание'
            FROM Stays s JOIN Guests g ON g.Id=s.GuestId JOIN Rooms r ON r.Id=s.RoomId WHERE s.Status='Проживает' ORDER BY s.PlannedCheckOutDate,r.Number
            """));
        SetGrid(_archiveGrid,Database.Query("""
            SELECT a.StayId AS Id,a.FullName AS 'Гость',a.RoomNumber AS 'Номер',a.CheckInDate AS 'Заезд',a.CheckOutDate AS 'Выезд',
                s.SettlementKopecks/100.0 AS 'Счёт',CASE WHEN s.SettlementKopecks IS NULL THEN 'Требуется сверка' WHEN s.IsLegacyEstimated=1 THEN 'Исторический тариф приблизителен' ELSE '' END AS 'Внимание'
            FROM GuestArchive a JOIN Stays s ON s.Id=a.StayId ORDER BY a.Id DESC
            """));
    }
    private void FindAvailableRooms() => RunSafely(()=>SetGrid(_availableRoomsGrid,HotelService.Available(_availableFromPicker.RequireDate("заезд"),_availableToPicker.RequireDate("выезд"),ComboId(_availableTypeCombo),(int)_availableCapacityNumeric.Value)));
    private void UseAvailableRoomForBooking()
    {
        var room=SelectedId(_availableRoomsGrid); if(room==0)return;
        ClearBookingForm(); _bookingRoomCombo.SelectedValue=room;
        _bookingFromPicker.Value=_availableFromPicker.Value; _bookingToPicker.Value=_availableToPicker.Value;
        _bookingSections.SelectedIndex=0;
    }
    private void LoadSelectedBooking()
    {
        if(_loading)return;var id=SelectedId(_bookingsGrid);if(id==0){if(_bookingEditId>0)ClearBookingForm();return;}
        var row=FirstRow(Database.Query("SELECT * FROM Bookings WHERE Id=@id",Database.P("id",id)));if(row is null)return;
        _bookingEditId=id;SelectComboValue(_bookingGuestCombo,row["GuestId"]);SelectComboValue(_bookingRoomCombo,row["RoomId"]);
        var from=ParseDate(row["DateFrom"]);var to=ParseDate(row["DateTo"]);
        _bookingFromPicker.SelectedDate=from>=Database.Today?from:null;
        _bookingToPicker.SelectedDate=from>=Database.Today?to:null;
        var status=Convert.ToString(row["Status"]);
        _bookingStatusCombo.DataSource=status is "Новое" or "Подтверждено"?new[]{"Новое","Подтверждено"}:new[]{status};
        _bookingStatusCombo.SelectedItem=status;_bookingStatusCombo.Enabled=status is "Новое" or "Подтверждено";
        _bookingCommentText.Text=Convert.ToString(row["Comment"]);
    }
    private void ClearBookingForm()
    {
        _bookingEditId=0;_bookingsGrid.ClearSelection();_bookingsGrid.CurrentCell=null;
        _bookingGuestCombo.SelectedIndex=-1;_bookingRoomCombo.SelectedIndex=-1;
        _bookingFromPicker.SelectedDate=null;_bookingToPicker.SelectedDate=null;
        _bookingStatusCombo.DataSource=new[]{"Новое","Подтверждено"};_bookingStatusCombo.Enabled=true;_bookingCommentText.Clear();
    }
    private void SaveBooking(bool update)=>RunSafely(()=>{
        if(update && _bookingEditId==0)throw new InvalidOperationException("Выберите бронь.");
        var id=HotelService.SaveBooking(update?_bookingEditId:0,ComboId(_bookingGuestCombo),ComboId(_bookingRoomCombo),_bookingFromPicker.RequireDate("заезд"),_bookingToPicker.RequireDate("выезд"),Convert.ToString(_bookingStatusCombo.SelectedItem)??"",_bookingCommentText.Text);
        _bookingEditId=id;RefreshAll();SelectGridId(_bookingsGrid,id);LoadSelectedBooking();
    });
    private void CancelBooking()=>RunSafely(()=>{
        if(_bookingEditId==0)throw new InvalidOperationException("Выберите бронь.");
        if(MessageBox.Show("Отменить выбранную бронь?",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        HotelService.CancelBooking(_bookingEditId);RefreshAll();
    });
    private void DeleteBooking()=>RunSafely(()=>{
        if(_bookingEditId==0)throw new InvalidOperationException("Выберите отменённую бронь.");
        if(MessageBox.Show("Удалить отменённую бронь без проживания?",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        HotelService.DeleteBooking(_bookingEditId);ClearBookingForm();RefreshAll();
    });
    private void CheckInSelectedBooking()=>RunSafely(()=>{
        var id=SelectedId(_pendingBookingsGrid);if(id==0)throw new InvalidOperationException("Выберите подтверждённую бронь, действующую сегодня.");
        HotelService.CheckIn(0,0,Database.Today,"Заселение по брони",id);RefreshAll();
    });
    private void CheckInWithoutBooking()=>RunSafely(()=>{
        HotelService.CheckIn(ComboId(_stayGuestCombo),ComboId(_stayRoomCombo),_stayPlannedOutPicker.RequireDate("плановый выезд"),_stayNotesText.Text);RefreshAll();
    });
    private void CheckoutSingleStay()=>RunSafely(()=>{
        var id=SelectedId(_staysGrid);if(id==0)throw new InvalidOperationException("Выберите активное проживание.");
        ShowCheckout(new[]{id});
    });
}
