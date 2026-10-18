using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Pages.Patients;

public partial class PetFileView : UserControl
{
    public PetFileView() => InitializeComponent();

    /// <summary>A visit row was pressed — open the visit detail sheet.</summary>
    private void OnVisitRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not VisitRowViewModel row)
        {
            return;
        }

        if (row.OpenVisit is { } open)
        {
            _ = open();
        }
    }
}
