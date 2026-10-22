using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Declarative day/week time grid shared by <see cref="DayGridView"/> and
/// <see cref="WeekGridView"/>. The column/event trees are data-bound in
/// <c>TimeGridSurface.axaml</c> (ItemsControl over
/// <see cref="AppointmentsCalendarViewModel.Columns"/>), so a DataContext change
/// no longer rebuilds the whole grid imperatively.
///
/// <para>
/// Direct-manipulation (drag a card to reschedule, drag an empty range to book)
/// is delegated to <see cref="TimeGridDragController"/>, which attaches a single
/// set of tunnelling pointer handlers and forwards every data change to the
/// ViewModel — the code-behind only owns its construction.
/// </para>
/// </summary>
public partial class TimeGridSurface : UserControl
{
    // Held for the control's lifetime so the handlers it registered stay alive.
    private readonly TimeGridDragController _dragController;

    public TimeGridSurface()
    {
        InitializeComponent();
        _dragController = new TimeGridDragController(this);
    }
}
