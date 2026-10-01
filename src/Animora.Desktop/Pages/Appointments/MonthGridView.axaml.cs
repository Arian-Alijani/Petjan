using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Month-view sub-view (mirrors the reference's <c>viewMonth()</c>): a 7-column
/// Jalali month grid with per-day chips, now declared entirely in
/// <c>MonthGridView.axaml</c> (data-bound over <see cref="AppointmentsCalendarViewModel.MonthCells"/>).
/// </summary>
public partial class MonthGridView : UserControl
{
    public MonthGridView() => InitializeComponent();

    /// <summary>
    /// Day-only mode: tapping the empty part of a day cell starts a booking for that day. Taps on an
    /// event chip are left to the chip's own command (open the appointment).
    /// </summary>
    private void OnCellTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not AppointmentsCalendarViewModel vm || !vm.CalendarModeIsDayOnly ||
            sender is not Control { DataContext: MonthCellViewModel cell })
        {
            return;
        }

        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        vm.OpenDayBookingCommand.Execute(cell.Day);
    }
}
