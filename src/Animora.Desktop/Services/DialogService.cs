using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Animora.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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

    // Pre-shell fallback (B03c): before MainWindow attaches the hosts (e.g. first-run from the login
    // window) a modal is shown in a lightweight owned Window instead of silently doing nothing.
    private Window? _fallbackWindow;

    /// <summary>Wires this service to the shell's overlay hosts. Call once, at composition time.</summary>
    public void Attach(ModalHost modalHost, DrawerHost drawerHost, SheetHost? sheetHost = null)
    {
        _modalHost = modalHost;
        _drawerHost = drawerHost;
        _sheetHost = sheetHost;
        // A modal is the top-most layer: dismissing its backdrop must not tear down a sheet/drawer below it.
        _modalHost.BackdropRequestedClose += (_, _) => CloseModal();
        _drawerHost.BackdropRequestedClose += (_, _) => Close();
        if (_sheetHost is not null)
        {
            _sheetHost.BackdropRequestedClose += (_, _) => Close();
        }
    }

    public void ShowModal(object content, double maxWidth = 520)
    {
        if (_modalHost is not null)
        {
            _modalHost.SetContent(content, maxWidth);
            return;
        }

        ShowFallbackWindow(content, maxWidth);
    }

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

    public async Task<IReadOnlyList<PickedFile>> PickFilesAsync(
        string title, IReadOnlyList<string> patterns, bool allowMultiple = true)
    {
        var host = (Control?)_sheetHost ?? (Control?)_modalHost ?? (Control?)_drawerHost;
        if (host is null || TopLevel.GetTopLevel(host)?.StorageProvider is not { } storage)
        {
            return [];
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter =
            [
                new FilePickerFileType("فایل‌های مجاز") { Patterns = patterns.ToList() },
            ],
        });

        var picked = new List<PickedFile>(files.Count);
        foreach (var file in files)
        {
            var properties = await file.GetBasicPropertiesAsync();
            var size = properties.Size is { } bytes ? (long?)checked((long)bytes) : null;
            picked.Add(new PickedFile(file.Name, size, () => file.OpenReadAsync()));
        }

        return picked;
    }

    public bool IsOverlayOpen =>
        _modalHost?.IsVisible == true || _drawerHost?.IsVisible == true || _sheetHost?.IsVisible == true
        || _fallbackWindow is not null;

    public void Close()
    {
        CloseModal();
        _drawerHost?.SetContent(null);
        _sheetHost?.SetContent(null);
    }

    public void CloseModal()
    {
        _modalHost?.SetContent(null);

        if (_fallbackWindow is { } window)
        {
            _fallbackWindow = null;
            window.Close();
        }
    }

    private void ShowFallbackWindow(object content, double maxWidth)
    {
        if (_fallbackWindow is not null)
        {
            _fallbackWindow.Content = content;
            return;
        }

        var window = new Window
        {
            Content = content,
            Width = maxWidth,
            MaxWidth = maxWidth,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = Avalonia.Media.FlowDirection.RightToLeft,
            ShowInTaskbar = false,
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_fallbackWindow, window))
            {
                _fallbackWindow = null;
            }

            window.Content = null;
        };
        _fallbackWindow = window;

        var owner = (global::Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is { IsVisible: true })
        {
            _ = ObserveDialogAsync(window.ShowDialog(owner));
        }
        else
        {
            window.Show();
        }
    }

    private static async Task ObserveDialogAsync(Task dialog)
    {
        try
        {
            await dialog;
        }
        catch (System.InvalidOperationException)
        {
            // The owner closed while the dialog was opening; nothing left to show.
        }
    }
}
