using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Controls;

/// <summary>
/// Centered dim-backdrop overlay host for modal content
/// (Animora-UI's .modal-backdrop/.modal). Managed by DialogService.
/// </summary>
public partial class ModalHost : UserControl
{
    public event EventHandler? BackdropRequestedClose;

    public ModalHost()
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
