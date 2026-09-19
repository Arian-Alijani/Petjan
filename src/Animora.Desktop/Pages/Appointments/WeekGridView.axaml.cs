using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Week-view sub-view (mirrors the reference's <c>viewWeek()</c>): seven day
/// columns with the shared time grid, Friday marked closed and the live "now"
/// line on today. Fully data-bound via <see cref="TimeGridSurface"/> — no
/// imperative rebuild on DataContext changes.
/// </summary>
public partial class WeekGridView : UserControl
{
    public WeekGridView() => InitializeComponent();
}
