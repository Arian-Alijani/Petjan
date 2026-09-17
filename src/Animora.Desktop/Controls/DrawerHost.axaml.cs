using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Controls;

/// <summary>
/// Right-anchored sliding overlay host for drawer content
/// (Animora-UI's side-drawer pattern, e.g. cheques add/edit drawer).
/// Managed by DialogService.
/// </summary>
public partial class DrawerHost : UserControl
{
    public event EventHandler? BackdropRequestedClose;

    public DrawerHost()
    {
        InitializeComponent();
    }

    public void SetContent(object? content)
    {
        ContentHost.Content = content;
        IsVisible = content is not null;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) =>
        BackdropRequestedClose?.Invoke(this, EventArgs.Empty);
}
