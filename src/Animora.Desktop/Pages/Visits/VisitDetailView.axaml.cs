using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Animora.Desktop.Pages.Visits;

/// <summary>The read-only viewer of one visit; the ViewModel owns every decision (CONVENTIONS.md §3).</summary>
public partial class VisitDetailView : UserControl
{
    public VisitDetailView()
    {
        InitializeComponent();
    }
}
