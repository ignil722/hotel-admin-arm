using HotelAdminArm.Data;
using HotelAdminArm.Domain;

namespace HotelAdminArm;

public sealed partial class MainForm
{
    private TabPage BuildServicesTab()
    {
        var page = CreateTabPage("Услуги");
        _servicesGrid = CreateGrid();
        _servicesGrid.CurrentCellChanged += (_, _) => RunSafely(LoadSelectedService);
        _ordersGrid = CreateGrid();

        _serviceNameText = TextBoxSingle();
        _servicePriceNumeric = MoneyNumeric();
        _serviceIsActiveCheck = new CheckBox { Text = "Услуга активна", Checked = true, AutoSize = true };
        _serviceMealCombo=Combo();_serviceMealCombo.DataSource=new[]{"Обычная услуга","Завтрак","Обед","Ужин"};
        _serviceRuleCombo=Combo();_serviceRuleCombo.DataSource=new[]{"Можно повторять","Один раз в день","Один раз за проживание"};
        _serviceMealCombo.SelectedIndexChanged+=(_,_)=>{if(_serviceMealCombo.SelectedIndex>0)_serviceRuleCombo.SelectedIndex=1;_serviceRuleCombo.Enabled=_serviceMealCombo.Enabled && _serviceMealCombo.SelectedIndex==0;};
        _serviceNameText.TextChanged+=(_,_)=>{
            if(_serviceEditId!=0 || _loading)return;
            var suggested=HotelService.SuggestedMealKind(_serviceNameText.Text);
            if(suggested!="None")_serviceMealCombo.SelectedIndex=Array.IndexOf(MealKinds,suggested);
        };

        var addServiceButton = CreateButton("Добавить услугу");
        addServiceButton.Click += (_, _) => SaveService(false);
        var updateServiceButton = CreateButton("Сохранить");
        updateServiceButton.Click += (_, _) => SaveService(true);
        var deleteServiceButton = CreateButton("Удалить");
        deleteServiceButton.Click += (_, _) => DeleteService();

        var clearService=CreateButton("Новая услуга");clearService.Click+=(_,_)=>ClearServiceForm();
        var serviceEditor = CreateEditorLayout(6);
        AddField(serviceEditor, "Название", _serviceNameText, 0);
        AddField(serviceEditor, "Цена", _servicePriceNumeric, 1);
        AddField(serviceEditor, "Статус", _serviceIsActiveCheck, 2);
        AddField(serviceEditor,"Категория",_serviceMealCombo,3);
        AddField(serviceEditor,"Повторение",_serviceRuleCombo,4);
        serviceEditor.Controls.Add(ButtonsPanel(clearService,addServiceButton,updateServiceButton,deleteServiceButton), 1, 5);

        _orderStayCombo = Combo();
        _orderServiceCombo = Combo();
        _orderQuantityNumeric = CountNumeric(100);
        _orderGuestCombo=Combo();
        _orderDatePicker=DatePicker(Database.Today);
        _orderDatePicker.DropDown+=(_,_)=>RunSafely(UpdateOrderRule);
        _orderRuleLabel=new Label{AutoSize=true,Dock=DockStyle.Fill};
        _orderStayCombo.SelectedValueChanged+=(_,_)=>{if(!_loading)RunSafely(()=>{RefreshOrderGuests();UpdateOrderRule();});};
        _orderServiceCombo.SelectedValueChanged+=(_,_)=>{if(!_loading)RunSafely(UpdateOrderRule);};
        var addOrderButton = CreateButton("Добавить заказ");
        addOrderButton.Click += (_, _) => AddServiceOrder();
        var deleteOrderButton = CreateButton("Удалить заказ");
        deleteOrderButton.Click += (_, _) => DeleteServiceOrder();

        var orderEditor = CreateEditorLayout(7);
        AddField(orderEditor, "Проживание", _orderStayCombo, 0);
        AddField(orderEditor, "Услуга", _orderServiceCombo, 1);
        AddField(orderEditor,"Получатель питания",_orderGuestCombo,2);
        AddField(orderEditor,"Дата питания",_orderDatePicker,3);
        AddField(orderEditor, "Количество", _orderQuantityNumeric, 4);
        AddField(orderEditor,"Правило",_orderRuleLabel,5);
        orderEditor.Controls.Add(ButtonsPanel(addOrderButton, deleteOrderButton), 1, 6);

        _serviceSections=Sections(("Заказы",Workspace(SearchableGrid(_ordersGrid),orderEditor)),("Каталог услуг",Workspace(SearchableGrid(_servicesGrid),serviceEditor)));
        page.Controls.Add(_serviceSections);
        return page;
    }

    private TabPage BuildPaymentsTab()
    {
        var page = CreateTabPage("Оплата");
        _paymentStaysGrid = CreateGrid(true);
        _paymentStaysGrid.SelectionChanged+=(_,_)=>{if(!_loading)CalculateSelectedPayment();};
        _paymentsGrid = CreateGrid();

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

        var details=CreateButton("Детали оплаты");details.Click+=(_,_)=>ShowPaymentDetails();
        var editor = CreateEditorLayout(4);
        AddField(editor, "Подтверждение", new Label {Text="Плательщик и способ выбираются в окне расчёта.",AutoSize=true,MaximumSize=new Size(350,0)}, 0);

        editor.Controls.Add(new Label(), 0, 2);
        editor.Controls.Add(_paymentTotalLabel, 1, 2);
        editor.Controls.Add(new Label(), 0, 3);
        editor.Controls.Add(ButtonsPanel(calculateButton,payButton), 1, 3);
        var hint=
            new Label
            {
                Text = "Для оплаты нескольких номеров выделите несколько строк в таблице активных проживаний.",
                Dock = DockStyle.Fill,
                AutoSize = true
            };
        editor.Controls.Add(hint,1,1);
        _paymentSections=Sections(("Расчёт и выезд",Workspace(SearchableGrid(_paymentStaysGrid),editor)),("История оплат",WithActions(SearchableGrid(_paymentsGrid),details)));
        page.Controls.Add(_paymentSections);
        return page;
    }

    private void RefreshServices()=>SetGrid(_servicesGrid,Database.Query("SELECT Id,Name AS 'Услуга',PriceKopecks/100.0 AS 'Цена',CASE IsActive WHEN 1 THEN 'Да' ELSE 'Нет' END AS 'Активна',CASE MealKind WHEN 'Breakfast' THEN 'Завтрак' WHEN 'Lunch' THEN 'Обед' WHEN 'Dinner' THEN 'Ужин' ELSE 'Обычная услуга' END AS 'Категория',CASE RepeatRule WHEN 'Daily' THEN 'Один раз в день' WHEN 'Once' THEN 'Один раз за проживание' ELSE 'Можно повторять' END AS 'Повторение' FROM Services ORDER BY Name"));
    private void RefreshOrders()=>SetGrid(_ordersGrid,Database.Query("""
        SELECT so.Id,r.Number AS 'Номер',g.LastName||' '||g.FirstName AS 'Гость',so.NameSnapshot AS 'Услуга',so.Quantity AS 'Кол-во',
            so.GuestNameSnapshot AS 'Получатель питания',so.UnitPriceKopecks/100.0 AS 'Цена',so.Quantity*so.UnitPriceKopecks/100.0 AS 'Сумма',so.OrderDate AS 'Дата',s.Status AS 'Проживание'
        FROM ServiceOrders so JOIN Stays s ON s.Id=so.StayId JOIN Rooms r ON r.Id=s.RoomId JOIN Guests g ON g.Id=s.GuestId ORDER BY so.Id DESC
        """));
    private void RefreshPayments()
    {
        SetGrid(_paymentStaysGrid,Database.Query("""
            SELECT s.Id,r.Number AS 'Номер',g.LastName||' '||g.FirstName AS 'Гость',s.CheckInDate AS 'Заезд',s.PlannedCheckOutDate AS 'Плановый выезд',
                (SELECT RateKopecks/100.0 FROM StaySegments WHERE StayId=s.Id AND DateTo IS NULL) AS 'Текущий тариф',
                COALESCE((SELECT SUM(Quantity*UnitPriceKopecks)/100.0 FROM ServiceOrders WHERE StayId=s.Id),0) AS 'Услуги',
                CASE s.IsLegacyEstimated WHEN 1 THEN 'Тариф восстановлен приблизительно' ELSE '' END AS 'Внимание'
            FROM Stays s JOIN Rooms r ON r.Id=s.RoomId JOIN Guests g ON g.Id=s.GuestId WHERE s.Status='Проживает' ORDER BY r.Number
            """));
        SetGrid(_paymentsGrid,Database.Query("""
            SELECT p.Id,p.PaymentDate AS 'Дата',p.PayerNameSnapshot AS 'Плательщик',p.AmountKopecks/100.0 AS 'Сумма',p.Method AS 'Способ',p.Comment AS 'Комментарий'
            FROM Payments p JOIN Guests g ON g.Id=p.PayerGuestId WHERE p.Finalized=1 ORDER BY p.Id DESC
            """));
    }
    private void LoadSelectedService()
    {
        if(_loading)return;var id=SelectedId(_servicesGrid);if(id==0){if(_serviceEditId>0)ClearServiceForm();return;}
        var row=FirstRow(Database.Query("SELECT * FROM Services WHERE Id=@id",Database.P("id",id)));if(row is null)return;
        _serviceEditId=id;_serviceNameText.Text=Convert.ToString(row["Name"]);_servicePriceNumeric.Value=HotelService.Rubles(Convert.ToInt64(row["PriceKopecks"]));_serviceIsActiveCheck.Checked=Convert.ToInt32(row["IsActive"])==1;
        _serviceMealCombo.SelectedIndex=Array.IndexOf(MealKinds,Convert.ToString(row["MealKind"]));
        _serviceRuleCombo.SelectedIndex=Array.IndexOf(RepeatRules,Convert.ToString(row["RepeatRule"]));
        var unused=Database.Scalar<long>("SELECT count(*) FROM ServiceOrders WHERE ServiceId=@id",Database.P("id",id))==0;
        _serviceMealCombo.Enabled=unused;_serviceRuleCombo.Enabled=unused && _serviceMealCombo.SelectedIndex==0;
    }
    private void ClearServiceForm(){_serviceEditId=0;_servicesGrid.ClearSelection();_servicesGrid.CurrentCell=null;_serviceNameText.Clear();_servicePriceNumeric.Value=0;_serviceIsActiveCheck.Checked=true;_serviceMealCombo.Enabled=true;_serviceMealCombo.SelectedIndex=0;_serviceRuleCombo.Enabled=true;_serviceRuleCombo.SelectedIndex=0;}
    private void SaveService(bool update)=>RunSafely(()=>{
        if(update&&_serviceEditId==0)throw new InvalidOperationException("Выберите услугу.");
        var id=HotelService.SaveService(update?_serviceEditId:0,_serviceNameText.Text,_servicePriceNumeric.Value,_serviceIsActiveCheck.Checked,MealKinds[_serviceMealCombo.SelectedIndex],RepeatRules[_serviceRuleCombo.SelectedIndex]);
        _serviceEditId=id;RefreshAll();SelectGridId(_servicesGrid,id);LoadSelectedService();
    });
    private void DeleteService()=>RunSafely(()=>{if(_serviceEditId==0)throw new InvalidOperationException("Выберите услугу.");_serviceIsActiveCheck.Checked=false;SaveService(true);});
    private static readonly string[] MealKinds={"None","Breakfast","Lunch","Dinner"};
    private static readonly string[] RepeatRules={"Repeatable","Daily","Once"};
    private void RefreshOrderGuests()
    {
        if(_orderGuestCombo is null)return;
        BindCombo(_orderGuestCombo,Database.Query("SELECT g.Id,g.LastName||' '||g.FirstName||' '||IFNULL(g.MiddleName,'') AS Name FROM StayGuests sg JOIN Guests g ON g.Id=sg.GuestId WHERE sg.StayId=@stay AND g.IsActive=1 ORDER BY Name",Database.P("stay",ComboId(_orderStayCombo))));
    }
    private void UpdateOrderRule()
    {
        if(_orderRuleLabel is null)return;
        var rows=Database.Query("SELECT MealKind,RepeatRule FROM Services WHERE Id=@id",Database.P("id",ComboId(_orderServiceCombo))).Rows;
        var meal=rows.Count>0 && Convert.ToString(rows[0]["MealKind"])!="None";
        var rule=rows.Count>0?Convert.ToString(rows[0]["RepeatRule"]):"Repeatable";
        _orderGuestCombo.Enabled=meal;
        var stayRows=Database.Query("SELECT CheckInDate,PlannedCheckOutDate FROM Stays WHERE Id=@id AND Status='Проживает'",Database.P("id",ComboId(_orderStayCombo))).Rows;
        var minimum=Database.Today;
        var mealKind=rows.Count>0?Convert.ToString(rows[0]["MealKind"])!:"None";
        // Время границы не включается: после него календарь начинается со следующего дня.
        var mealEnded=meal && Database.Now.TimeOfDay>=HotelService.MealDeadline(mealKind);
        if(mealEnded)minimum=minimum.AddDays(1);
        var maximum=Database.Today.AddDays(-1);
        if(stayRows.Count>0)
        {
            var checkIn=DateTime.Parse(Convert.ToString(stayRows[0]["CheckInDate"])!);
            if(checkIn>minimum)minimum=checkIn;
            maximum=DateTime.Parse(Convert.ToString(stayRows[0]["PlannedCheckOutDate"])!).AddDays(-1);
        }
        _orderDatePicker.Enabled=meal && maximum>=minimum;
        var previous=_orderDatePicker.Value.Date;
        _orderDatePicker.MaxDate=DateTimePicker.MaximumDateTime;
        _orderDatePicker.MinDate=DateTimePicker.MinimumDateTime;
        if(_orderDatePicker.Enabled)
        {
            _orderDatePicker.MinDate=minimum;_orderDatePicker.MaxDate=maximum;
            _orderDatePicker.Value=previous>=minimum && previous<=maximum?previous:minimum;
        }
        else _orderDatePicker.Value=Database.Today;
        var limited=meal || rule!="Repeatable";
        if(limited)_orderQuantityNumeric.Value=1;
        _orderQuantityNumeric.Maximum=limited?1:100;_orderQuantityNumeric.Enabled=!limited;
        _orderRuleLabel.Text=rows.Count==0?"Выберите услугу":meal?"Одна порция каждого вида питания на гостя в день":rule=="Daily"?"Один раз в день на проживание":rule=="Once"?"Один раз за проживание":"Повторение разрешено, количество от 1 до 100";
        if(meal)
        {
            _orderRuleLabel.Text+=". Дата питания — до дня выезда (не включительно).";
            _orderRuleLabel.Text+=$" На сегодня — до {HotelService.MealDeadline(mealKind):hh\\:mm}.";
            if(!_orderDatePicker.Enabled)_orderRuleLabel.Text+=stayRows.Count==0?" Выберите проживание.":" Нет доступных дней питания. Продлите проживание или выберите другую услугу.";
            else if(mealEnded)_orderRuleLabel.Text+=" Сегодня приём заказов на это питание завершён.";
        }
    }
    private void AddServiceOrder()=>RunSafely(()=>{
        if(_orderGuestCombo.Enabled && ComboId(_orderGuestCombo)==0)throw new InvalidOperationException("Выберите получателя питания из проживающих гостей.");
        if(_orderGuestCombo.Enabled && !_orderDatePicker.Enabled)throw new InvalidOperationException("Нет доступных дней питания. Проверьте проживание и при необходимости продлите его.");
        HotelService.AddOrder(ComboId(_orderStayCombo),ComboId(_orderServiceCombo),(int)_orderQuantityNumeric.Value,_orderGuestCombo.Enabled?ComboId(_orderGuestCombo):null,_orderGuestCombo.Enabled?_orderDatePicker.Value.Date:null);RefreshAll();
    });
    private void DeleteServiceOrder()=>RunSafely(()=>{
        var id=SelectedId(_ordersGrid);if(id==0)throw new InvalidOperationException("Выберите запись услуги.");
        if(MessageBox.Show("Удалить услугу из незакрытого счёта?",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        HotelService.DeleteOrder(id);RefreshAll();
    });
    private int[] SelectedStayIdsForPayment()=>_paymentStaysGrid.SelectedRows.Cast<DataGridViewRow>().Select(r=>Convert.ToInt32(r.Cells["Id"].Value)).Distinct().ToArray();
    private void CalculateSelectedPayment()
    {
        var ids=SelectedStayIdsForPayment();
        if(ids.Length==0){_paymentTotalLabel.Text="Итого: 0 руб.";return;}
        try{_paymentTotalLabel.Text=$"Итого: {HotelService.Rubles(HotelService.Preview(ids).Total):N2} руб.";}
        catch(Exception){_paymentTotalLabel.Text="Счёт изменён — обновите список";}
    }
    private void PaySelectedStays()=>RunSafely(()=>ShowCheckout(SelectedStayIdsForPayment()));
}
