using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Animora.Desktop.Pages.Patients;

public partial class OwnerSheetView : UserControl
{
    public OwnerSheetView() => InitializeComponent();

    /// <summary>
    /// Click-to-copy: a row whose <see cref="Control.Tag"/> carries the value copies it to
    /// the clipboard (needs the visual tree → <see cref="TopLevel"/>) and asks the VM to toast.
    /// </summary>
    private async void OnCopyRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border row
            || DataContext is not OwnerSheetViewModel vm
            || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var value = row.Tag as string;
        if (!vm.CanCopy(value))
        {
            return;
        }

        var label = (row.Child as Grid)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
        e.Handled = true;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            await clipboard.SetTextAsync(value);
            vm.NotifyCopied(label);
        }
        catch (Exception)
        {
            // Clipboard can be transiently locked by another process; a missed copy is non-fatal.
        }
    }
}
