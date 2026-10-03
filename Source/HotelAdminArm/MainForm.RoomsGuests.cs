using System.Data;
using HotelAdminArm.Data;

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
        _roomsGrid.SelectionChanged += (_, _) => LoadSelectedRoom();
        _roomsGrid.Dock = DockStyle.Fill;

        _roomNumberText = TextBoxSingle();
        _roomTypeCombo = Combo();
        _roomFloorNumeric = CountNumeric(50);
        _roomCapacityNumeric = CountNumeric(10);
        _roomPriceNumeric = MoneyNumeric();
        _roomStatusCombo = Combo();
        _roomStatusCombo.DataSource = new[] { "Свободен", "Занят", "Ремонт" };
        _roomNotesText = TextBoxMulti();
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
        var deleteButton = CreateButton("Удалить");
        deleteButton.Click += (_, _) => DeleteRoom();
        var clearButton = CreateButton("Очистить");
        clearButton.Click += (_, _) => ClearRoomForm();

        var editor = CreateEditorLayout(10);
        AddField(editor, "Номер", _roomNumberText, 0);
        AddField(editor, "Тип", _roomTypeCombo, 1);
        AddField(editor, "Этаж", _roomFloorNumeric, 2);
        AddField(editor, "Мест", _roomCapacityNumeric, 3);
        AddField(editor, "Цена/сутки", _roomPriceNumeric, 4);
        AddField(editor, "Статус", _roomStatusCombo, 5);
        AddField(editor, "Примечание", _roomNotesText, 6);
        AddField(editor, "Фото", _roomPhotoBox, 7);
        editor.Controls.Add(new Label(), 0, 8);
        editor.Controls.Add(choosePhotoButton, 1, 8);
        editor.Controls.Add(new Label(), 0, 9);
        editor.Controls.Add(ButtonsPanel(addButton, updateButton, deleteButton, clearButton), 1, 9);

        editor.Dock = DockStyle.Right;
        editor.Width = 460;
        editor.MinimumSize = new Size(440, 0);
        layout.Controls.Add(_roomsGrid);
        layout.Controls.Add(editor);
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
        _guestsGrid.SelectionChanged += (_, _) => LoadSelectedGuest();
        _guestsGrid.Dock = DockStyle.Fill;

        _guestLastNameText = TextBoxSingle();
        _guestFirstNameText = TextBoxSingle();
        _guestMiddleNameText = TextBoxSingle();
        _guestBirthDatePicker = DatePicker(DateTime.Today.AddYears(-25));
        _guestPassportText = TextBoxSingle();
        _guestPhoneText = TextBoxSingle();
        _guestEmailText = TextBoxSingle();
        _guestAddressText = TextBoxMulti();

        var addButton = CreateButton("Добавить");
        addButton.Click += (_, _) => SaveGuest(false);
        var updateButton = CreateButton("Сохранить");
        updateButton.Click += (_, _) => SaveGuest(true);
        var deleteButton = CreateButton("Удалить");
        deleteButton.Click += (_, _) => DeleteGuest();
        var clearButton = CreateButton("Очистить");
        clearButton.Click += (_, _) => ClearGuestForm();

        var editor = CreateEditorLayout(9);
        AddField(editor, "Фамилия", _guestLastNameText, 0);
        AddField(editor, "Имя", _guestFirstNameText, 1);
        AddField(editor, "Отчество", _guestMiddleNameText, 2);
        AddField(editor, "Дата рождения", _guestBirthDatePicker, 3);
        AddField(editor, "Паспорт", _guestPassportText, 4);
        AddField(editor, "Телефон", _guestPhoneText, 5);
        AddField(editor, "Email", _guestEmailText, 6);
        AddField(editor, "Адрес", _guestAddressText, 7);
        editor.Controls.Add(new Label(), 0, 8);
        editor.Controls.Add(ButtonsPanel(addButton, updateButton, deleteButton, clearButton), 1, 8);

        editor.Dock = DockStyle.Right;
        editor.Width = 460;
        editor.MinimumSize = new Size(440, 0);
        layout.Controls.Add(_guestsGrid);
        layout.Controls.Add(editor);
        page.Controls.Add(layout);
        return page;
    }

    private void RefreshRooms()
    {
        SetGrid(
            _roomsGrid,
            Database.Query(
                """
                SELECT r.Id, r.Number AS 'Номер', rt.Name AS 'Тип', r.Floor AS 'Этаж',
                       r.Capacity AS 'Мест', r.DailyPrice AS 'Цена за сутки',
                       r.Status AS 'Статус', r.Notes AS 'Примечание'
                FROM Rooms r
                JOIN RoomTypes rt ON rt.Id = r.RoomTypeId
                ORDER BY r.Number;
                """));
    }

    private void RefreshGuests()
    {
        SetGrid(
            _guestsGrid,
            Database.Query(
                """
                SELECT Id, LastName AS 'Фамилия', FirstName AS 'Имя', MiddleName AS 'Отчество',
                       BirthDate AS 'Дата рождения', Passport AS 'Паспорт',
                       Phone AS 'Телефон', Email, Address AS 'Адрес'
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
            return;
        }

        var row = FirstRow(Database.Query("SELECT * FROM Rooms WHERE Id = @id;", Database.P("id", id)));
        if (row is null)
        {
            return;
        }

        _roomNumberText.Text = Convert.ToString(row["Number"]);
        SelectComboValue(_roomTypeCombo, row["RoomTypeId"]);
        _roomFloorNumeric.Value = Convert.ToDecimal(row["Floor"]);
        _roomCapacityNumeric.Value = Convert.ToDecimal(row["Capacity"]);
        _roomPriceNumeric.Value = Convert.ToDecimal(row["DailyPrice"]);
        _roomStatusCombo.SelectedItem = Convert.ToString(row["Status"]);
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
            return;
        }

        var row = FirstRow(Database.Query("SELECT * FROM Guests WHERE Id = @id;", Database.P("id", id)));
        if (row is null)
        {
            return;
        }

        _guestLastNameText.Text = Convert.ToString(row["LastName"]);
        _guestFirstNameText.Text = Convert.ToString(row["FirstName"]);
        _guestMiddleNameText.Text = Convert.ToString(row["MiddleName"]);
        _guestBirthDatePicker.Value = ParseDate(row["BirthDate"]);
        _guestPassportText.Text = Convert.ToString(row["Passport"]);
        _guestPhoneText.Text = Convert.ToString(row["Phone"]);
        _guestEmailText.Text = Convert.ToString(row["Email"]);
        _guestAddressText.Text = Convert.ToString(row["Address"]);
    }

    private void SaveRoom(bool update)
    {
        RunSafely(() =>
        {
            if (string.IsNullOrWhiteSpace(_roomNumberText.Text))
            {
                throw new InvalidOperationException("Введите номер комнаты.");
            }

            if (update)
            {
                var id = SelectedId(_roomsGrid);
                if (id == 0)
                {
                    throw new InvalidOperationException("Выберите номер для изменения.");
                }

                Database.Execute(
                    """
                    UPDATE Rooms
                       SET Number = @number, RoomTypeId = @roomTypeId, Floor = @floor,
                           Capacity = @capacity, DailyPrice = @dailyPrice, Status = @status,
                           Notes = @notes, Photo = @photo
                     WHERE Id = @id;
                    """,
                    Database.P("number", _roomNumberText.Text.Trim()),
                    Database.P("roomTypeId", ComboId(_roomTypeCombo)),
                    Database.P("floor", (int)_roomFloorNumeric.Value),
                    Database.P("capacity", (int)_roomCapacityNumeric.Value),
                    Database.P("dailyPrice", _roomPriceNumeric.Value),
                    Database.P("status", Convert.ToString(_roomStatusCombo.SelectedItem)),
                    Database.P("notes", _roomNotesText.Text.Trim()),
                    Database.P("photo", _selectedRoomPhoto),
                    Database.P("id", id));
            }
            else
            {
                Database.Execute(
                    """
                    INSERT INTO Rooms (Number, RoomTypeId, Floor, Capacity, DailyPrice, Status, Notes, Photo)
                    VALUES (@number, @roomTypeId, @floor, @capacity, @dailyPrice, @status, @notes, @photo);
                    """,
                    Database.P("number", _roomNumberText.Text.Trim()),
                    Database.P("roomTypeId", ComboId(_roomTypeCombo)),
                    Database.P("floor", (int)_roomFloorNumeric.Value),
                    Database.P("capacity", (int)_roomCapacityNumeric.Value),
                    Database.P("dailyPrice", _roomPriceNumeric.Value),
                    Database.P("status", Convert.ToString(_roomStatusCombo.SelectedItem)),
                    Database.P("notes", _roomNotesText.Text.Trim()),
                    Database.P("photo", _selectedRoomPhoto));
            }

            RefreshAll();
            MessageBox.Show("Данные номера сохранены.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void SaveGuest(bool update)
    {
        RunSafely(() =>
        {
            if (string.IsNullOrWhiteSpace(_guestLastNameText.Text) ||
                string.IsNullOrWhiteSpace(_guestFirstNameText.Text) ||
                string.IsNullOrWhiteSpace(_guestPassportText.Text))
            {
                throw new InvalidOperationException("Введите фамилию, имя и паспорт гостя.");
            }

            var sql = update
                ? """
                  UPDATE Guests
                     SET LastName = @lastName, FirstName = @firstName, MiddleName = @middleName,
                         BirthDate = @birthDate, Passport = @passport, Phone = @phone,
                         Email = @email, Address = @address
                   WHERE Id = @id;
                  """
                : """
                  INSERT INTO Guests (LastName, FirstName, MiddleName, BirthDate, Passport, Phone, Email, Address)
                  VALUES (@lastName, @firstName, @middleName, @birthDate, @passport, @phone, @email, @address);
                  """;

            var id = SelectedId(_guestsGrid);
            if (update && id == 0)
            {
                throw new InvalidOperationException("Выберите гостя для изменения.");
            }

            var parameters = new List<Microsoft.Data.Sqlite.SqliteParameter>
            {
                Database.P("lastName", _guestLastNameText.Text.Trim()),
                Database.P("firstName", _guestFirstNameText.Text.Trim()),
                Database.P("middleName", _guestMiddleNameText.Text.Trim()),
                Database.P("birthDate", _guestBirthDatePicker.Value),
                Database.P("passport", _guestPassportText.Text.Trim()),
                Database.P("phone", _guestPhoneText.Text.Trim()),
                Database.P("email", _guestEmailText.Text.Trim()),
                Database.P("address", _guestAddressText.Text.Trim())
            };

            if (update)
            {
                parameters.Add(Database.P("id", id));
            }

            Database.Execute(sql, parameters.ToArray());
            RefreshAll();
            MessageBox.Show("Данные гостя сохранены.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void DeleteRoom()
    {
        RunSafely(() =>
        {
            var id = SelectedId(_roomsGrid);
            if (id == 0)
            {
                return;
            }

            if (MessageBox.Show("Удалить выбранный номер?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Database.Execute("DELETE FROM Rooms WHERE Id = @id;", Database.P("id", id));
                RefreshAll();
            }
        });
    }

    private void DeleteGuest()
    {
        RunSafely(() =>
        {
            var id = SelectedId(_guestsGrid);
            if (id == 0)
            {
                return;
            }

            if (MessageBox.Show("Удалить выбранного гостя?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Database.Execute("DELETE FROM Guests WHERE Id = @id;", Database.P("id", id));
                RefreshAll();
            }
        });
    }

    private void ClearRoomForm()
    {
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
        _guestLastNameText.Clear();
        _guestFirstNameText.Clear();
        _guestMiddleNameText.Clear();
        _guestBirthDatePicker.Value = DateTime.Today.AddYears(-25);
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

        _selectedRoomPhoto = File.ReadAllBytes(dialog.FileName);
        ShowRoomPhoto();
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
