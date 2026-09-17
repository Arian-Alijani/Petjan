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

    /// <summary>
    /// Shows <paramref name="content"/> in the centered modal panel, capped at
    /// <paramref name="maxWidth"/> (reference <c>.modal</c> = 520;
    /// <c>.modal.wide</c> = 760 — used by the owner/pet wizard + add-pet modal).
    /// Passing null content hides the host.
    /// </summary>
    public void SetContent(object? content, double maxWidth = 520)
    {
        if (content is not null)
        {
            Panel.MaxWidth = maxWidth;
        }

        ContentHost.Content = content;
        IsVisible = content is not null;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) =>
        BackdropRequestedClose?.Invoke(this, EventArgs.Empty);
}
