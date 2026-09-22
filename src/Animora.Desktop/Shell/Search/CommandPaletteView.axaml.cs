using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Animora.Desktop.Shell.Search;

/// <summary>
/// Code-behind for the Ctrl+K command palette. Keeps only thin input plumbing
/// that cannot live in XAML: row hover/click routing to the ViewModel, global
/// keyboard handling (↑/↓/Enter/Esc) via a tunneling handler so the input box
/// never swallows them, and initial focus of the search input.
/// </summary>
public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();

        // Tunnel so the commands run before the focused TextBox consumes them.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // Autofocus the input once the palette is in the visual tree.
        AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => Input.Focus(), DispatcherPriority.Input);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                vm.CloseCommand.Execute(null);
                break;
            case Key.Down:
                e.Handled = true;
                vm.MoveDown();
                break;
            case Key.Up:
                e.Handled = true;
                vm.MoveUp();
                break;
            case Key.Enter:
                e.Handled = true;
                vm.ActivateCommand.Execute(null);
                break;
        }
    }

    private void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm && sender is Control { DataContext: CommandPaletteItemViewModel item })
        {
            vm.Highlight(item);
        }
    }

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm && sender is Control { DataContext: CommandPaletteItemViewModel item })
        {
            e.Handled = true;
            vm.ChooseCommand.Execute(item);
        }
    }
}
