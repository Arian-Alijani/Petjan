using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Controls;

/// <summary>
/// Centered dim-backdrop overlay host for the Patients records "sheet" pattern
/// (Animora-UI's <c>.sheet-root</c> / <c>.sheet.owner-sheet</c> /
/// <c>.sheet.pet-sheet</c>). Unlike <see cref="ModalHost"/> (a fixed-width
/// dialog) and <see cref="DrawerHost"/> (a right-anchored panel), a sheet is a
/// large, centered, elevated surface whose size differs per record — the owner
/// file is 680×760, the patient file 1120×880. Managed by DialogService via
/// <see cref="SetContent(object?, double, double)"/>.
/// </summary>
public partial class SheetHost : UserControl
{
    public event EventHandler? BackdropRequestedClose;

    public SheetHost()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows <paramref name="content"/> inside a centered sheet of the given
    /// width/height (transcribed from the reference's <c>.owner-sheet</c> /
    /// <c>.pet-sheet</c> rules) and reveals the host. Passing null content hides
    /// the host.
    /// </summary>
    public void SetContent(object? content, double width = 680, double height = 760)
    {
        if (content is not null)
        {
            Panel.Width = width;
            Panel.Height = height;
            ContentHost.Content = content;
        }

        IsVisible = content is not null;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) =>
        BackdropRequestedClose?.Invoke(this, EventArgs.Empty);
}
