using Animora.Desktop.Services;
using Avalonia.Controls;

namespace Animora.Desktop.Controls;

/// <summary>
/// Corner-anchored overlay rendering <see cref="ToastService.Toasts"/>.
/// Placed once, at shell/window root, on top of everything else.
/// </summary>
public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
    }

    /// <summary>Binds this host to a live toast service's message collection.</summary>
    public void Attach(ToastService toastService) => Items.ItemsSource = toastService.Toasts;
}
