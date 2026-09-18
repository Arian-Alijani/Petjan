using Avalonia.Controls;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// Code-behind for the Phase 11 clinical-encounter wizard shell. Per
/// CONVENTIONS.md §3 it contains no business logic — the ViewModel drives
/// every step, and this control only wires the generated XAML.
/// </summary>
public partial class VisitEncounterView : UserControl
{
    public VisitEncounterView()
    {
        InitializeComponent();
    }
}
