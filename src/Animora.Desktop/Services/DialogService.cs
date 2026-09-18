using System.IO;
using System.Text;
using System.Threading.Tasks;
using Animora.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

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

    public void ShowDrawer(object content, double width = 420) => _drawerHost?.SetContent(content, width);

    public void ShowSheet(object content, double width = 680, double height = 760) =>
        _sheetHost?.SetContent(content, width, height);

    public async Task<bool> SaveTextFileAsync(string suggestedName, string content, string extension = "csv")
    {
        var host = (Control?)_sheetHost ?? (Control?)_modalHost ?? (Control?)_drawerHost;
        if (host is null || TopLevel.GetTopLevel(host)?.StorageProvider is not { } storage)
        {
            return false;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "ذخیره فایل",
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType(extension.ToUpperInvariant())
                {
                    Patterns = ["*." + extension],
                },
            ],
        });

        if (file is null)
        {
            return false;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        await writer.WriteAsync(content);
        await writer.FlushAsync();
        return true;
    }

    public void Close()
    {
        _modalHost?.SetContent(null);
        _drawerHost?.SetContent(null);
        _sheetHost?.SetContent(null);
    }
}
