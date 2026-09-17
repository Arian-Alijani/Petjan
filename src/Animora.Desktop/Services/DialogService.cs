using Animora.Desktop.Controls;

namespace Animora.Desktop.Services;

/// <summary>
/// Implementation of <see cref="IDialogService"/>. Holds no UI itself —
/// it drives whichever <see cref="ModalHost"/>/<see cref="DrawerHost"/>/
/// <see cref="SheetHost"/> instance is attached at the shell/window root via
/// <see cref="Attach"/>.
/// </summary>
public sealed class DialogService : IDialogService
{
    private ModalHost? _modalHost;
    private DrawerHost? _drawerHost;
    private SheetHost? _sheetHost;

    /// <summary>Wires this service to the shell's overlay hosts. Call once, at composition time.</summary>
    public void Attach(ModalHost modalHost, DrawerHost drawerHost, SheetHost? sheetHost = null)
    {
        _modalHost = modalHost;
        _drawerHost = drawerHost;
        _sheetHost = sheetHost;
        _modalHost.BackdropRequestedClose += (_, _) => Close();
        _drawerHost.BackdropRequestedClose += (_, _) => Close();
        if (_sheetHost is not null)
        {
            _sheetHost.BackdropRequestedClose += (_, _) => Close();
        }
    }

    public void ShowModal(object content, double maxWidth = 520) =>
        _modalHost?.SetContent(content, maxWidth);

    public void ShowDrawer(object content) => _drawerHost?.SetContent(content);

    public void ShowSheet(object content, double width = 680, double height = 760) =>
        _sheetHost?.SetContent(content, width, height);

    public void Close()
    {
        _modalHost?.SetContent(null);
        _drawerHost?.SetContent(null);
        _sheetHost?.SetContent(null);
    }
}
