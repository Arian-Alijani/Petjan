using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Controls;

/// <summary>
/// A small reusable Persian (Jalali) date-picker control, mirroring the
/// reference's mini calendar widget (<c>dpHtml</c> / <c>bindDp</c> in
/// Animora-UI/assets/cheques-ui.js): a read-only field that opens a 262px
/// popup with a month header, a 7-column Saturday-first grid and day cells.
/// It is generically reusable (Cheques, Appointments, Boarding, …) so it lives
/// here in <c>Controls</c> rather than under a single page.
///
/// The picker transports dates as ASCII <c>yyyy/MM/dd</c> Jalali strings
/// (<see cref="DateValue"/>) and renders Persian digits through the Phase 04
/// helpers. Day arithmetic goes through <see cref="JalaliCalendar"/>. Every
/// bindable member is a styled property so the compiled bindings update.
/// </summary>
public partial class JalaliDatePicker : UserControl
{
    /// <summary>The selected date as an ASCII <c>yyyy/MM/dd</c> Jalali string (two-way).</summary>
    public static readonly StyledProperty<string> DateValueProperty =
        AvaloniaProperty.Register<JalaliDatePicker, string>(nameof(DateValue), string.Empty);

    /// <summary>The read-only field's display text (Persian digits).</summary>
    public static readonly StyledProperty<string> DateTextProperty =
        AvaloniaProperty.Register<JalaliDatePicker, string>(nameof(DateText), string.Empty);

    /// <summary>«مرداد ۱۴۰۵» — the popup header (the <c>dp-head .t</c>).</summary>
    public static readonly StyledProperty<string> HeaderTextProperty =
        AvaloniaProperty.Register<JalaliDatePicker, string>(nameof(HeaderText), string.Empty);

    /// <summary>Whether the popup grid is open (two-way bound to the Popup).</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<JalaliDatePicker, bool>(nameof(IsOpen));

    private int _viewYear;
    private int _viewMonth;

    public JalaliDatePicker()
    {
        InitializeComponent();
        SelectDayCommand = new RelayCommand<JalaliDayCell?>(SelectDay);
        SyncViewFromValue();
        Rebuild();
    }

    /// <summary>
    /// The selected date as an ASCII <c>yyyy/MM/dd</c> Jalali string. Two-way
    /// bindable so a host form can read/write it.
    /// </summary>
    public string DateValue
    {
        get => GetValue(DateValueProperty);
        set => SetValue(DateValueProperty, value);
    }

    /// <summary>The read-only field's display text (Persian digits), mirroring <c>fa(val)</c>.</summary>
    public string DateText
    {
        get => GetValue(DateTextProperty);
        private set => SetValue(DateTextProperty, value);
    }

    /// <summary>«مرداد ۱۴۰۵» — the popup header (mirrors the <c>dp-head .t</c>).</summary>
    public string HeaderText
    {
        get => GetValue(HeaderTextProperty);
        private set => SetValue(HeaderTextProperty, value);
    }

    /// <summary>Whether the popup grid is open.</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>The day cells + leading blanks of the visible month (mirrors <c>dp-grid</c>).</summary>
    public ObservableCollection<JalaliDayCell> Days { get; } = [];

    /// <summary>Picks a day (closes the popup and commits the value).</summary>
    public IRelayCommand<JalaliDayCell?> SelectDayCommand { get; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DateValueProperty)
        {
            SyncViewFromValue();
            Rebuild();
        }
    }

    private void OnInputClick(object? sender, RoutedEventArgs e)
    {
        SyncViewFromValue();
        Rebuild();
        IsOpen = !IsOpen;
    }

    private void OnPrevMonthClick(object? sender, RoutedEventArgs e)
    {
        (_viewYear, _viewMonth) = JalaliCalendar.ShiftMonth(_viewYear, _viewMonth, -1);
        Rebuild();
    }

    private void OnNextMonthClick(object? sender, RoutedEventArgs e)
    {
        (_viewYear, _viewMonth) = JalaliCalendar.ShiftMonth(_viewYear, _viewMonth, 1);
        Rebuild();
    }

    private void SelectDay(JalaliDayCell? cell)
    {
        if (cell is null || cell.IsBlank)
        {
            return;
        }

        DateValue = cell.Iso;
        IsOpen = false;
    }

    private void SyncViewFromValue()
    {
        var raw = string.IsNullOrWhiteSpace(DateValue) ? JalaliCalendar.Today : DateValue;
        try
        {
            var parsed = JalaliCalendar.Parse(raw);
            _viewYear = parsed.Year;
            _viewMonth = parsed.Month;
        }
        catch (FormatException)
        {
            var today = JalaliCalendar.Parse(JalaliCalendar.Today);
            _viewYear = today.Year;
            _viewMonth = today.Month;
        }
    }

    private void Rebuild()
    {
        Days.Clear();

        var firstDay = $"{_viewYear:D4}/{_viewMonth:D2}/01";
        var lead = JalaliCalendar.Weekday(firstDay);
        for (var i = 0; i < lead; i++)
        {
            Days.Add(new JalaliDayCell(true, string.Empty, string.Empty, false, false));
        }

        var today = JalaliCalendar.Today;
        var selected = DateValue;
        var len = JalaliCalendar.MonthLen(_viewYear, _viewMonth);
        for (var d = 1; d <= len; d++)
        {
            var iso = $"{_viewYear:D4}/{_viewMonth:D2}/{d:D2}";
            Days.Add(new JalaliDayCell(
                false,
                PersianNumberFormatter.ToPersianDigits(d.ToString(CultureInfo.InvariantCulture)),
                iso,
                iso == selected,
                iso == today));
        }

        DateText = string.IsNullOrWhiteSpace(DateValue) ? string.Empty : JalaliCalendar.Short(DateValue);
        HeaderText =
            $"{PersianMonthNames.Get(_viewMonth)} {PersianNumberFormatter.ToPersianDigits(_viewYear)}";
    }
}

/// <summary>
/// One cell of the <see cref="JalaliDatePicker"/> grid: a leading blank, or a
/// day with its Persian-digit label, ASCII ISO value and selected/today flags.
/// </summary>
public sealed class JalaliDayCell
{
    public JalaliDayCell(bool isBlank, string text, string iso, bool isSelected, bool isToday)
    {
        IsBlank = isBlank;
        Text = text;
        Iso = iso;
        IsSelected = isSelected;
        IsToday = isToday;
    }

    public bool IsBlank { get; }

    public string Text { get; }

    public string Iso { get; }

    public bool IsSelected { get; }

    public bool IsToday { get; }
}
