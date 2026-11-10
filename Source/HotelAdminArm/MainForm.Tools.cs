using System.Data;
using System.Drawing.Printing;
using HotelAdminArm.Data;
using HotelAdminArm.Domain;

namespace HotelAdminArm;

public sealed partial class MainForm
{
    internal void SelectRoomForScreenshot(){if(_roomsGrid.Rows.Count>0)SelectGridId(_roomsGrid,Convert.ToInt32(_roomsGrid.Rows[0].Cells["Id"].Value));}
    private DataGridView _calendarGrid=null!, _warningsGrid=null!, _todayGrid=null!;
    private OperationalDatePicker _calendarFrom=null!;
    private readonly Dictionary<DataGridView,TextBox> _searchBoxes=new();

    private static string FriendlyError(Exception error)
    {
        if(error is Microsoft.Data.Sqlite.SqliteException sql)
        {
            if(sql.SqliteErrorCode==19 && sql.Message.Contains("UNIQUE"))return "Такая запись уже существует: проверьте номер, паспорт, услугу или оплату.";
            if(sql.SqliteErrorCode==19 && sql.Message.Contains("FOREIGN KEY"))return "Запись связана с историей гостиницы. Используйте отключение или архивирование.";
            if(sql.SqliteErrorCode is 5 or 6)return "База занята другим экземпляром программы. Повторите операцию после обновления списка.";
        }
        return error.Message;
    }
    private static void SelectGridId(DataGridView grid,int id)
    {
        grid.ClearSelection();
        foreach(DataGridViewRow row in grid.Rows)
            if(Convert.ToInt32(row.Cells["Id"].Value)==id){grid.CurrentCell=row.Cells.Cast<DataGridViewCell>().First(c=>c.Visible);row.Selected=true;return;}
        grid.CurrentCell=null;
    }
    private Control SearchableGrid(DataGridView grid)
    {
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,38));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var search=new TextBox{Dock=DockStyle.Fill,PlaceholderText="Поиск по списку...",Margin=new Padding(4,6,4,4)};
        _searchBoxes[grid]=search;
        search.TextChanged+=(_,_)=>ApplySearch(grid);
        grid.DataSourceChanged+=(_,_)=>ApplySearch(grid);
        panel.Controls.Add(search,0,0);panel.Controls.Add(grid,0,1);return panel;
    }
    private void ApplySearch(DataGridView grid)
    {
        if(grid.DataSource is not DataTable table || !_searchBoxes.TryGetValue(grid,out var box))return;
        var pattern=string.Concat(box.Text.Select(ch=>ch switch{'\''=>"''",'['=>"[[]",']'=>"[]]",'%'=>"[%]",'*'=>"[*]",_=>ch.ToString()}));
        table.DefaultView.RowFilter=pattern.Length==0?"":string.Join(" OR ",table.Columns.Cast<DataColumn>().Where(c=>c.ColumnName!="Id").Select(c=>$"CONVERT([{c.ColumnName.Replace("]","\\]")}], 'System.String') LIKE '%{pattern}%'"));
    }
    private TabPage BuildCalendarTab()
    {
        var page=CreateTabPage("Календарь");
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        _calendarFrom=new OperationalDatePicker(Database.Today);var refresh=CreateButton("Показать 30 дней");refresh.Click+=(_,_)=>RunSafely(RefreshCalendar);
        panel.Controls.Add(ButtonsPanel(new Label{Text="Начало периода",AutoSize=true},_calendarFrom,refresh),0,0);
        panel.Controls.Add(new Label{Text="Зелёный: свободен; синий: бронь; красный: проживание; серый: ремонт/неактивен. Двойной щелчок по свободной дате открывает новую бронь.",AutoSize=true,MaximumSize=new Size(1100,0)},0,1);
        _calendarGrid=CreateGrid();_calendarGrid.Tag="Calendar";_calendarGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;_calendarGrid.AutoGenerateColumns=false;
        _calendarGrid.CellDoubleClick+=(_,e)=>RunSafely(()=>{
            if(e.RowIndex<0||e.ColumnIndex<2)return;
            var cell=_calendarGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];if(Convert.ToString(cell.Value)!="Свободен")return;
            var date=(DateTime)_calendarGrid.Columns[e.ColumnIndex].Tag!;
            ClearBookingForm();_bookingRoomCombo.SelectedValue=Convert.ToInt32(_calendarGrid.Rows[e.RowIndex].Cells["Id"].Value);
            _bookingFromPicker.Value=date;_bookingToPicker.Value=date.AddDays(1);_tabs.SelectedIndex=3;_bookingSections.SelectedIndex=0;
        });
        panel.Controls.Add(_calendarGrid,0,2);page.Controls.Add(panel);return page;
    }
    private void RefreshCalendar()
    {
        var start=_calendarFrom.RequireDate("начало периода");
        _calendarFrom.Value=start;
        var rooms=Database.Query("SELECT Id,Number,IsActive,UnderMaintenance FROM Rooms ORDER BY Number");
        var stays=Database.Query("SELECT RoomId,CheckInDate,PlannedCheckOutDate FROM Stays WHERE Status='Проживает'");
        var table=new DataTable();table.Columns.Add("Id",typeof(int));table.Columns.Add("Номер",typeof(string));
        for(var d=0;d<30;d++)table.Columns.Add(start.AddDays(d).ToString("dd.MM"),typeof(string));
        var available=Enumerable.Range(0,30).Select(d=>HotelService.Available(start.AddDays(d),start.AddDays(d+1)).AsEnumerable().Select(r=>Convert.ToInt32(r["Id"])).ToHashSet()).ToArray();
        foreach(DataRow room in rooms.Rows)
        {
            var id=Convert.ToInt32(room["Id"]);var row=table.NewRow();row[0]=id;row[1]=room["Number"];
            for(var d=0;d<30;d++)
            {
                var date=start.AddDays(d);
                row[d+2]=Convert.ToInt32(room["IsActive"])==0?"Неактивен":Convert.ToInt32(room["UnderMaintenance"])==1?"Ремонт":available[d].Contains(id)?"Свободен":
                    stays.AsEnumerable().Any(s=>Convert.ToInt32(s["RoomId"])==id&&ParseDate(s["CheckInDate"])<date.AddDays(1)&&(ParseDate(s["PlannedCheckOutDate"])<=Database.Today||date<ParseDate(s["PlannedCheckOutDate"])))?"Занят":"Бронь";
            }
            table.Rows.Add(row);
        }
        _calendarGrid.DataSource=null;_calendarGrid.Columns.Clear();
        foreach(DataColumn column in table.Columns)_calendarGrid.Columns.Add(new DataGridViewTextBoxColumn{Name=column.ColumnName,HeaderText=column.ColumnName,DataPropertyName=column.ColumnName,Visible=column.ColumnName!="Id",Frozen=column.Ordinal<2});
        SetGrid(_calendarGrid,table);_calendarGrid.Columns["Номер"]!.Width=75;
        for(var d=0;d<30;d++){_calendarGrid.Columns[d+2].Width=115;_calendarGrid.Columns[d+2].Tag=start.AddDays(d);}
        foreach(DataGridViewRow row in _calendarGrid.Rows)foreach(DataGridViewCell cell in row.Cells.Cast<DataGridViewCell>().Skip(2))
            cell.Style.BackColor=Convert.ToString(cell.Value) switch{"Свободен"=>Color.Honeydew,"Бронь"=>Color.AliceBlue,"Занят"=>Color.MistyRose,_=>Color.Gainsboro};
    }
    private TabPage BuildOperationsTab()
    {
        var page=CreateTabPage("Контроль и копии");var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,32));panel.RowStyles.Add(new RowStyle(SizeType.Percent,40));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,32));panel.RowStyles.Add(new RowStyle(SizeType.Percent,60));
        var backup=CreateButton("Резервная копия");backup.Click+=(_,_)=>RunSafely(()=>{
            using var dialog=new SaveFileDialog{Filter="База SQLite|*.db",FileName=$"hotel-{DateTime.Now:yyyyMMdd-HHmmss}.db",OverwritePrompt=false};
            if(dialog.ShowDialog(this)==DialogResult.OK){Database.BackupTo(dialog.FileName);MessageBox.Show("Резервная копия сохранена.",Text);}
        });
        var restore=CreateButton("Восстановить копию");restore.Click+=(_,_)=>RunSafely(()=>{
            using var dialog=new OpenFileDialog{Filter="База SQLite|*.db"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            if(MessageBox.Show("Восстановить выбранную копию? Текущая база будет предварительно сохранена. Закройте другие экземпляры программы.",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            Database.RestoreFrom(dialog.FileName);ClearRoomForm();ClearGuestForm();ClearBookingForm();ClearServiceForm();RefreshAll();
        });
        var refresh=CreateButton("Обновить всё");refresh.Click+=(_,_)=>RefreshAll();
        var archive=CreateButton("Счёт из архива");archive.Click+=(_,_)=>RunSafely(()=>{
            var id=SelectedId(_archiveGrid);if(id==0)throw new InvalidOperationException("Выберите выезд во вкладке «Заселение и архив».");ShowText(HotelService.ClosedBill(id),true);
        });
        panel.Controls.Add(ButtonsPanel(refresh,backup,restore,archive),0,0);
        panel.Controls.Add(CreateSectionLabel("Сегодня: заезды и выезды"),0,1);_todayGrid=CreateGrid();panel.Controls.Add(EmptyAwareGrid(_todayGrid,"Сегодня нет заездов и выездов"),0,2);
        panel.Controls.Add(CreateSectionLabel("Требуется внимание / сверка истории"),0,3);_warningsGrid=CreateGrid();panel.Controls.Add(EmptyAwareGrid(_warningsGrid,"Проблем не обнаружено"),0,4);
        page.Controls.Add(panel);return page;
    }
    private void RefreshExtras()
    {
        SetGrid(_warningsGrid,HotelService.Warnings());
        SetGrid(_todayGrid,Database.Query("""
            SELECT b.Id,'Заезд' AS 'Операция',r.Number AS 'Номер',g.LastName||' '||g.FirstName AS 'Гость',b.Status AS 'Статус'
            FROM Bookings b JOIN Rooms r ON r.Id=b.RoomId JOIN Guests g ON g.Id=b.GuestId WHERE b.DateFrom=hotel_today() AND b.Status IN('Новое','Подтверждено')
            UNION ALL SELECT s.Id,'Выезд',r.Number,g.LastName||' '||g.FirstName,'Проживает'
            FROM Stays s JOIN Rooms r ON r.Id=s.RoomId JOIN Guests g ON g.Id=s.GuestId WHERE s.PlannedCheckOutDate=hotel_today() AND s.Status='Проживает'
            """));
        _stayCheckInPicker.Value=Database.Today;
        if(_tabs.SelectedTab?.Text=="Календарь"||_calendarGrid.DataSource is null)RefreshCalendar();
    }
    private Form Dialog(string title,int width=650,int height=460)=>new(){Text=title,StartPosition=FormStartPosition.CenterParent,Size=new Size(width,height),MinimumSize=new Size(500,300),Font=Font,ShowInTaskbar=false};
    private void ShowCheckout(int[] ids)
    {
        var bill=HotelService.Preview(ids);
        using var dialog=Dialog("Подтверждение расчёта",760,650);
        var payer=Combo();BindCombo(payer,GuestsLookup());payer.SelectedIndex=-1;
        var method=Combo();method.DataSource=new[]{"Наличные","Банковская карта","Перевод"};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(12)};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));for(var i=0;i<3;i++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var text=HotelBranding.HotelName+"\nРасчёт на "+Database.Today.ToString("dd.MM.yyyy")+"\n\n"+bill.Text;
        if(Database.Scalar<long>("SELECT count(*) FROM Stays WHERE IsLegacyEstimated=1 AND Id IN("+string.Join(",",ids.Distinct())+")")>0)
            text+="\nВнимание: тарифы старых проживаний восстановлены приблизительно. Проверьте их перед оплатой.";
        layout.Controls.Add(new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Text=text},0,0);
        layout.Controls.Add(ButtonsPanel(new Label{Text="Плательщик",AutoSize=true},payer),0,1);
        layout.Controls.Add(ButtonsPanel(new Label{Text="Способ",AutoSize=true},method),0,2);
        var complete=CreateButton(bill.Total==0?"Закрыть бесплатное проживание":"Подтвердить оплату и выезд");
        complete.Click+=(_,_)=>RunSafely(()=>{
            if(ComboId(payer)==0)throw new InvalidOperationException("Выберите плательщика.");
            if(MessageBox.Show($"Плательщик: {payer.Text}\nСпособ: {method.Text}\nСумма: {HotelService.Rubles(bill.Total):N2} руб.\nПодтвердить выезд?",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            HotelService.Checkout(ids,ComboId(payer),method.Text,bill.Fingerprint);dialog.DialogResult=DialogResult.OK;
        });
        var print=CreateButton("Предпросмотр / печать");print.Click+=(_,_)=>RunSafely(()=>PrintText(text));
        layout.Controls.Add(ButtonsPanel(complete,print),0,3);dialog.Controls.Add(layout);
        if(dialog.ShowDialog(this)==DialogResult.OK)RefreshAll();
    }
    private void ExtendSelectedStay()=>RunSafely(()=>{
        var id=SelectedId(_staysGrid);if(id==0)throw new InvalidOperationException("Выберите проживание.");
        using var dialog=Dialog("Продлить проживание",540,220);var date=new OperationalDatePicker(Database.Today.AddDays(1), minimumDays: 1);
        var old=Database.Scalar<string>("SELECT PlannedCheckOutDate FROM Stays WHERE Id=@id",Database.P("id",id));date.Value=DateTime.Parse(old)>Database.Today?DateTime.Parse(old).AddDays(1):Database.Today.AddDays(1);
        var save=CreateButton("Продлить");save.Click+=(_,_)=>RunSafely(()=>{HotelService.Extend(id,date.Value);dialog.DialogResult=DialogResult.OK;});
        dialog.Controls.Add(ButtonsPanel(new Label{Text="Новый плановый выезд",AutoSize=true},date,save));if(dialog.ShowDialog(this)==DialogResult.OK)RefreshAll();
    });
    private void MoveSelectedStay()=>RunSafely(()=>{
        var id=SelectedId(_staysGrid);if(id==0)throw new InvalidOperationException("Выберите проживание.");
        var to=DateTime.Parse(Database.Scalar<string>("SELECT PlannedCheckOutDate FROM Stays WHERE Id=@id",Database.P("id",id)));
        var capacity=Database.Scalar<int>("SELECT count(*) FROM StayGuests WHERE StayId=@id",Database.P("id",id));
        var rooms=HotelService.Available(Database.Today,to,0,capacity);rooms.Columns.Add("Name",typeof(string));foreach(DataRow row in rooms.Rows)row["Name"]=$"{row["Номер"]} — {Convert.ToDecimal(row["Цена за сутки"]):N2} руб./сутки";
        using var dialog=Dialog("Переселение с сохранением истории",600,240);var room=Combo();BindCombo(room,rooms);room.SelectedIndex=-1;
        var save=CreateButton("Переселить");save.Click+=(_,_)=>RunSafely(()=>{HotelService.Move(id,ComboId(room));dialog.DialogResult=DialogResult.OK;});
        dialog.Controls.Add(ButtonsPanel(new Label{Text="Новый номер и его текущий тариф",AutoSize=true},room,save));if(dialog.ShowDialog(this)==DialogResult.OK)RefreshAll();
    });
    private void ManageOccupants()=>RunSafely(()=>{
        var id=SelectedId(_staysGrid);if(id==0)throw new InvalidOperationException("Выберите проживание.");
        using var dialog=Dialog("Проживающие в номере",650,450);var grid=CreateGrid();var guest=Combo();BindCombo(guest,GuestsLookup());guest.SelectedIndex=-1;
        void Reload()=>SetGrid(grid,Database.Query("SELECT g.Id,g.LastName||' '||g.FirstName AS 'Гость',g.Passport AS 'Паспорт' FROM StayGuests sg JOIN Guests g ON g.Id=sg.GuestId WHERE sg.StayId=@id",Database.P("id",id)));
        Reload();var add=CreateButton("Добавить гостя");add.Click+=(_,_)=>RunSafely(()=>{HotelService.AddOccupant(id,ComboId(guest));Reload();});
        var remove=CreateButton("Удалить гостя");remove.Click+=(_,_)=>RunSafely(()=>{HotelService.RemoveOccupant(id,SelectedId(grid));Reload();});
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.Controls.Add(grid,0,0);panel.Controls.Add(ButtonsPanel(guest,add,remove),0,1);
        dialog.Controls.Add(panel);dialog.ShowDialog(this);RefreshAll();
    });
    private void ShowPaymentDetails()=>RunSafely(()=>{
        var id=SelectedId(_paymentsGrid);if(id==0)throw new InvalidOperationException("Выберите платёж.");
        var payment=Database.Query("SELECT p.*,p.PayerNameSnapshot AS Payer FROM Payments p JOIN Guests g ON g.Id=p.PayerGuestId WHERE p.Id=@id",Database.P("id",id)).Rows[0];
        var text=$"{HotelBranding.HotelName}\nОплата #{id} от {payment["PaymentDate"]}\nПлательщик: {payment["Payer"]}\nСпособ: {payment["Method"]}\nСумма: {HotelService.Rubles(Convert.ToInt64(payment["AmountKopecks"])):N2} руб.\n\n";
        foreach(DataRow stay in Database.Query("SELECT StayId FROM PaymentStays WHERE PaymentId=@id",Database.P("id",id)).Rows)text+=HotelService.ClosedBill(Convert.ToInt32(stay[0]))+"\n\n";
        ShowText(text,true);
    });
    private void ShowText(string text,bool printable,string title="Счёт и история")
    {
        using var dialog=Dialog(title,800,650);var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new TextBox{Text=text,ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both},0,0);
        if(printable){var print=CreateButton("Предпросмотр / печать");print.Click+=(_,_)=>RunSafely(()=>PrintText(text));panel.Controls.Add(ButtonsPanel(print),0,1);}
        dialog.Controls.Add(panel);dialog.ShowDialog(this);
    }
    internal static PrintDocument InvoiceDocument(string text)
    {
        var document=new PrintDocument();var font=new Font("Segoe UI",10);
        document.Disposed+=(_,_)=>font.Dispose();
        var remaining=text;document.BeginPrint+=(_,_)=>remaining=text;
        document.PrintPage+=(_,e)=>{
            using var format=new StringFormat(StringFormatFlags.LineLimit);
            var graphics=e.Graphics ?? throw new InvalidOperationException("Принтер не предоставил область печати.");
            graphics.MeasureString(remaining,font,e.MarginBounds.Size,format,out var characters,out var lineCount);
            if(characters==0)throw new InvalidOperationException("Область печати слишком мала.");
            graphics.DrawString(remaining[..characters],font,Brushes.Black,e.MarginBounds,format);
            remaining=remaining[characters..];e.HasMorePages=remaining.Length>0;
        };
        return document;
    }
    private void PrintText(string text)
    {
        using var document=InvoiceDocument(text);
        using var preview=new PrintPreviewDialog{Document=document,Width=1000,Height=750};preview.ShowDialog(this);
    }
}
