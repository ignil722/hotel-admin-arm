using System.Data;
using HotelAdminArm.Data;
using HotelAdminArm.Domain;

namespace HotelAdminArm;

public sealed partial class MainForm
{
    private TabPage BuildRoomsTab()
    {
        var page = CreateTabPage("Номера");
        var layout = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = HotelBranding.Background
        };

        _roomsGrid = CreateGrid();
        _roomsGrid.CurrentCellChanged += (_, _) => RunSafely(LoadSelectedRoom);
        _roomsGrid.Dock = DockStyle.Fill;

        _roomNumberText = TextBoxSingle();
        _roomTypeCombo = Combo();
        _roomFloorNumeric = CountNumeric(50);
        _roomCapacityNumeric = CountNumeric(10);
        _roomPriceNumeric = MoneyNumeric();
        _roomStatusCombo = Combo();
        _roomStatusCombo.DataSource = new[] { "Доступен", "Ремонт" };
        _roomNotesText = TextBoxMulti();
        _roomActiveCheck = new CheckBox { Text="Номер активен",Checked=true,AutoSize=true };
        _roomPhotoBox = new PictureBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Height = 220,
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = HotelBranding.SurfaceAlt
        };

        var choosePhotoButton = CreateButton("Выбрать фото");
        choosePhotoButton.Click += (_, _) => ChooseRoomPhoto();
        var addButton = CreateButton("Добавить");
        addButton.Click += (_, _) => SaveRoom(false);
        var updateButton = CreateButton("Сохранить");
        updateButton.Click += (_, _) => SaveRoom(true);
        var deleteButton = CreateButton("Архивировать");
        deleteButton.Click += (_, _) => DeleteRoom();
        var clearButton = CreateButton("Новая запись");
        clearButton.Click += (_, _) => ClearRoomForm();

        var editor = CreateEditorLayout(10);
        AddField(editor, "Номер", _roomNumberText, 0);
        AddField(editor, "Тип", _roomTypeCombo, 1);
        AddField(editor, "Этаж", _roomFloorNumeric, 2);
        AddField(editor, "Мест", _roomCapacityNumeric, 3);
        AddField(editor, "Цена/сутки", _roomPriceNumeric, 4);
        AddField(editor, "Состояние", ButtonsPanel(_roomStatusCombo,_roomActiveCheck), 5);
        AddField(editor, "Примечание", _roomNotesText, 6);
        AddField(editor, "Фото", _roomPhotoBox, 7);
        editor.Controls.Add(new Label(), 0, 8);
        editor.Controls.Add(choosePhotoButton, 1, 8);
        editor.Controls.Add(new Label(), 0, 9);
        editor.Controls.Add(ButtonsPanel(addButton, updateButton, deleteButton, clearButton), 1, 9);

        layout.Controls.Add(Workspace(SearchableGrid(_roomsGrid),editor));
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildGuestsTab()
    {
        var page = CreateTabPage("Гости");
        var layout = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = HotelBranding.Background
        };

        _guestsGrid = CreateGrid();
        _guestsGrid.CurrentCellChanged += (_, _) => RunSafely(LoadSelectedGuest);
        _guestsGrid.Dock = DockStyle.Fill;

        _guestLastNameText = TextBoxSingle();
        _guestFirstNameText = TextBoxSingle();
        _guestMiddleNameText = TextBoxSingle();
        _guestBirthDatePicker = DatePicker(Database.Today.AddYears(-25));
        _guestBirthDatePicker.ShowCheckBox=true;
        _guestActiveCheck=new CheckBox {Text="Гость активен",Checked=true,AutoSize=true};
        _guestPassportText = TextBoxSingle();
        _guestPhoneText = TextBoxSingle();
        _guestEmailText = TextBoxSingle();
        _guestAddressText = TextBoxMulti();

        var addButton = CreateButton("Добавить");
        addButton.Click += (_, _) => SaveGuest(false);
        var updateButton = CreateButton("Сохранить");
        updateButton.Click += (_, _) => SaveGuest(true);
        var deleteButton = CreateButton("Архивировать");
        deleteButton.Click += (_, _) => DeleteGuest();
        var clearButton = CreateButton("Новая запись");
        clearButton.Click += (_, _) => ClearGuestForm();

        var editor = CreateEditorLayout(9);
        AddField(editor, "Фамилия", _guestLastNameText, 0);
        AddField(editor, "Имя", _guestFirstNameText, 1);
        AddField(editor, "Отчество", _guestMiddleNameText, 2);
        AddField(editor, "Дата рождения", ButtonsPanel(_guestBirthDatePicker,_guestActiveCheck), 3);
        AddField(editor, "Паспорт", _guestPassportText, 4);
        AddField(editor, "Телефон", _guestPhoneText, 5);
        AddField(editor, "Email", _guestEmailText, 6);
        AddField(editor, "Адрес", _guestAddressText, 7);
        editor.Controls.Add(new Label(), 0, 8);
        editor.Controls.Add(ButtonsPanel(addButton, updateButton, deleteButton, clearButton), 1, 8);

        layout.Controls.Add(Workspace(SearchableGrid(_guestsGrid),editor));
        page.Controls.Add(layout);
        return page;
    }

    private void RefreshRooms() => SetGrid(_roomsGrid, HotelService.Rooms());

    private void RefreshGuests()
    {
        SetGrid(
            _guestsGrid,
            Database.Query(
                """
                SELECT Id, LastName AS 'Фамилия', FirstName AS 'Имя', MiddleName AS 'Отчество',
                       BirthDate AS 'Дата рождения', Passport AS 'Паспорт',
                       Phone AS 'Телефон', Email, Address AS 'Адрес', CASE IsActive WHEN 1 THEN 'Да' ELSE 'Нет' END AS 'Активен'
                FROM Guests
                ORDER BY LastName, FirstName;
                """));
    }

    private void LoadSelectedRoom()
    {
        if (_loading)
        {
            return;
        }

        var id = SelectedId(_roomsGrid);
        if (id == 0)
        {
            if(_roomEditId>0)ClearRoomForm();
            return;
        }

        var row = FirstRow(Database.Query("SELECT * FROM Rooms WHERE Id = @id;", Database.P("id", id)));
        if (row is null)
        {
            return;
        }

        _roomEditId=id;
        _roomActiveCheck.Checked=Convert.ToInt32(row["IsActive"])==1;
        _roomNumberText.Text = Convert.ToString(row["Number"]);
        SelectComboValue(_roomTypeCombo, row["RoomTypeId"]);
        _roomFloorNumeric.Value = Convert.ToDecimal(row["Floor"]);
        _roomCapacityNumeric.Value = Convert.ToDecimal(row["Capacity"]);
        _roomPriceNumeric.Value = HotelService.Rubles(Convert.ToInt64(row["PriceKopecks"]));
        _roomStatusCombo.SelectedIndex = Convert.ToInt32(row["UnderMaintenance"]);
        _roomNotesText.Text = Convert.ToString(row["Notes"]);

        _selectedRoomPhoto = row["Photo"] == DBNull.Value ? null : (byte[])row["Photo"];
        ShowRoomPhoto();
    }

    private void LoadSelectedGuest()
    {
        if (_loading)
        {
            return;
        }

        var id = SelectedId(_guestsGrid);
        if (id == 0)
        {
            if(_guestEditId>0)ClearGuestForm();
            return;
        }

        var row = FirstRow(Database.Query("SELECT * FROM Guests WHERE Id = @id;", Database.P("id", id)));
        if (row is null)
        {
            return;
        }

        _guestEditId=id;
        _guestActiveCheck.Checked=Convert.ToInt32(row["IsActive"])==1;
        _guestLastNameText.Text = Convert.ToString(row["LastName"]);
        _guestFirstNameText.Text = Convert.ToString(row["FirstName"]);
        _guestMiddleNameText.Text = Convert.ToString(row["MiddleName"]);
        _guestBirthDatePicker.Value = ParseDate(row["BirthDate"]);
        _guestBirthDatePicker.Checked=row["BirthDate"]!=DBNull.Value;
        _guestPassportText.Text = Convert.ToString(row["Passport"]);
        _guestPhoneText.Text = Convert.ToString(row["Phone"]);
        _guestEmailText.Text = Convert.ToString(row["Email"]);
        _guestAddressText.Text = Convert.ToString(row["Address"]);
    }

    private void SaveRoom(bool update) => RunSafely(() => {
        if(update && _roomEditId==0)throw new InvalidOperationException("Выберите номер для изменения.");
        var id=HotelService.SaveRoom(update?_roomEditId:0,_roomNumberText.Text,ComboId(_roomTypeCombo),(int)_roomFloorNumeric.Value,(int)_roomCapacityNumeric.Value,_roomPriceNumeric.Value,_roomStatusCombo.SelectedIndex==1,_roomActiveCheck.Checked,_roomNotesText.Text,_selectedRoomPhoto);
        _roomEditId=id; RefreshAll(); SelectGridId(_roomsGrid,id); LoadSelectedRoom();
    });

    private void SaveGuest(bool update) => RunSafely(() => {
        if(update && _guestEditId==0)throw new InvalidOperationException("Выберите гостя для изменения.");
        var id=HotelService.SaveGuest(update?_guestEditId:0,_guestLastNameText.Text,_guestFirstNameText.Text,_guestMiddleNameText.Text,_guestBirthDatePicker.Checked?_guestBirthDatePicker.Value.Date:null,_guestPassportText.Text,_guestPhoneText.Text,_guestEmailText.Text,_guestAddressText.Text,_guestActiveCheck.Checked);
        _guestEditId=id; RefreshAll(); SelectGridId(_guestsGrid,id); LoadSelectedGuest();
    });

    private void DeleteRoom() => RunSafely(() => {
        if(_roomEditId==0)return;
        if(MessageBox.Show("Сделать номер неактивным? История сохранится.",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        _roomActiveCheck.Checked=false; SaveRoom(true);
    });

    private void DeleteGuest() => RunSafely(() => {
        if(_guestEditId==0)return;
        if(MessageBox.Show("Сделать гостя неактивным? История сохранится.",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        _guestActiveCheck.Checked=false; SaveGuest(true);
    });

    private void ClearRoomForm()
    {
        _roomEditId=0; _roomsGrid.ClearSelection(); _roomsGrid.CurrentCell=null; _roomActiveCheck.Checked=true;
        _roomNumberText.Clear();
        _roomFloorNumeric.Value = 1;
        _roomCapacityNumeric.Value = 1;
        _roomPriceNumeric.Value = 0;
        _roomStatusCombo.SelectedIndex = 0;
        _roomNotesText.Clear();
        _selectedRoomPhoto = null;
        ShowRoomPhoto();
    }

    private void ClearGuestForm()
    {
        _guestEditId=0; _guestsGrid.ClearSelection(); _guestsGrid.CurrentCell=null; _guestActiveCheck.Checked=true;
        _guestBirthDatePicker.Checked=false;
        _guestLastNameText.Clear();
        _guestFirstNameText.Clear();
        _guestMiddleNameText.Clear();
        _guestBirthDatePicker.Value = Database.Today.AddYears(-25);
        _guestPassportText.Clear();
        _guestPhoneText.Clear();
        _guestEmailText.Clear();
        _guestAddressText.Clear();
    }

    private void ChooseRoomPhoto()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение номера",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp|Все файлы|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RunSafely(() => {
            if(new FileInfo(dialog.FileName).Length>10*1024*1024)throw new InvalidOperationException("Изображение должно быть не больше 10 МБ.");
            using var source=Image.FromFile(dialog.FileName);
            if((long)source.Width*source.Height>40000000)throw new InvalidOperationException("Изображение слишком большое.");
            using var scaled=new Bitmap(source,new Size(Math.Max(1,source.Width* Math.Min(1600,source.Width)/source.Width),Math.Max(1,source.Height*Math.Min(1600,source.Width)/source.Width)));
            using var stream=new MemoryStream(); scaled.Save(stream,System.Drawing.Imaging.ImageFormat.Png);
            _selectedRoomPhoto=stream.ToArray(); ShowRoomPhoto();
        });
    }

    private void ShowRoomPhoto()
    {
        _roomPhotoBox.Image?.Dispose();
        _roomPhotoBox.Image = null;

        if (_selectedRoomPhoto is null)
        {
            return;
        }

        using var stream = new MemoryStream(_selectedRoomPhoto);
        using var sourceImage = Image.FromStream(stream);
        _roomPhotoBox.Image = new Bitmap(sourceImage);
    }
}
