using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Shared "no appointments" placeholder reused by the day/week time grid and the
/// month grid. Purely declarative (no logic) — the host toggles its
/// <see cref="Visual.IsVisible"/> from <c>!HasGridEvents</c> / <c>!HasMonthEvents</c>
/// so the empty state no longer has to be rebuilt imperatively on every render.
/// </summary>
public partial class CalendarEmptyState : UserControl
{
    public CalendarEmptyState() => InitializeComponent();
}
