using HotelAdminArm.Data;

namespace HotelAdminArm;

// Operational dates use the computer's local day; historical dates and birthdays
// are displayed separately and do not use this control.
internal sealed class OperationalDatePicker : DateTimePicker
{
    private readonly bool _allowEmpty;
    private readonly int _minimumDays;
    private bool _adjusting;
    private bool _hasDate;

    internal OperationalDatePicker(DateTime? initial = null, bool allowEmpty = false, int minimumDays = 0)
    {
        _allowEmpty = allowEmpty;
        _minimumDays = minimumDays;
        Width = 150;
        Format = DateTimePickerFormat.Custom;
        SynchronizeDateBounds();
        SelectedDate = initial ?? (allowEmpty ? null : MinDate);
    }

    internal DateTime? SelectedDate
    {
        get => _hasDate ? base.Value.Date : null;
        set
        {
            SynchronizeDateBounds();
            if (value is null)
            {
                if (!_allowEmpty) throw new InvalidOperationException("Выберите дату.");
                _hasDate = false;
                _adjusting = true;
                try { base.Value = MinDate; }
                finally { _adjusting = false; }
                CustomFormat = " ";
                return;
            }
            base.Value = value.Value.Date;
            _hasDate = true;
            CustomFormat = "dd.MM.yyyy";
        }
    }

    // Assigning a date from a search result or the booking table is an explicit
    // selection even when it equals the underlying (hidden) default value.
    public new DateTime Value
    {
        get => base.Value;
        set => SelectedDate = value;
    }

    internal DateTime RequireDate(string field)
    {
        SynchronizeDateBounds();
        return SelectedDate ?? throw new InvalidOperationException($"Выберите дату: {field}.");
    }

    internal void SynchronizeDateBounds()
    {
        var minimum = Database.Today.AddDays(_minimumDays);
        var expired = _hasDate && base.Value.Date < minimum;
        _adjusting = true;
        try
        {
            MinDate = minimum;
            if (!_hasDate || expired) base.Value = minimum;
            if (expired && _allowEmpty) _hasDate = false;
            CustomFormat = _hasDate ? "dd.MM.yyyy" : " ";
        }
        finally { _adjusting = false; }
    }

    protected override void OnDropDown(EventArgs eventargs)
    {
        SynchronizeDateBounds();
        base.OnDropDown(eventargs);
    }

    protected override void OnValueChanged(EventArgs eventargs)
    {
        if (_adjusting) return;
        _hasDate = true;
        CustomFormat = "dd.MM.yyyy";
        base.OnValueChanged(eventargs);
    }

    protected override void OnCloseUp(EventArgs eventargs)
    {
        // Today is already the hidden default in an empty control. Closing the
        // opened calendar must also allow choosing this unchanged date.
        if (!_hasDate)
        {
            _hasDate = true;
            CustomFormat = "dd.MM.yyyy";
            base.OnValueChanged(EventArgs.Empty);
        }
        base.OnCloseUp(eventargs);
    }
}
