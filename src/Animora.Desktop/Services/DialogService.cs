using Animora.Desktop.Controls;
using Avalonia.Controls;

namespace Animora.Desktop.Services;

/// <summary>
/// Implementation of <see cref="IDialogService"/>. Holds no UI itself —
/// it drives whichever <see cref="ModalHost"/>/<see cref="DrawerHost"/>
/// instance is attached at the shell/window root via <see cref="Attach"/>.
/// </summary>
public sealed class DialogService : IDialogService
{
    private ModalHost? _modalHost;
    private DrawerHost? _drawerHost;

    /// <summary>Wires this service to the shell's overlay hosts. Call once, at composition time.</summary>
    public void Attach(ModalHost modalHost, DrawerHost drawerHost)
    {
        _modalHost = modalHost;
        _drawerHost = drawerHost;
        _modalHost.BackdropRequestedClose += (_, _) => Close();
        _drawerHost.BackdropRequestedClose += (_, _) => Close();
    }

    public void ShowModal(object content) => _modalHost?.SetContent(content);

    public void ShowDrawer(object content) => _drawerHost?.SetContent(content);

    public void Close()
    {
        _modalHost?.SetContent(null);
        _drawerHost?.SetContent(null);
    }
}
