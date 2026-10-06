using System.Collections.Generic;
using Animora.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Animora.Desktop.Shell;

public partial class MainWindow : Window
{
    // Alt+digit → the 1-based quick-access section index. Uses the physical
    // digit keys plus the numpad digits so either works.
    private static readonly Dictionary<Key, int> AltSections = new()
    {
        [Key.D1] = 1, [Key.NumPad1] = 1,
        [Key.D2] = 2, [Key.NumPad2] = 2,
        [Key.D3] = 3, [Key.NumPad3] = 3,
        [Key.D4] = 4, [Key.NumPad4] = 4,
        [Key.D5] = 5, [Key.NumPad5] = 5,
        [Key.D6] = 6, [Key.NumPad6] = 6,
        [Key.D7] = 7, [Key.NumPad7] = 7,
        [Key.D8] = 8, [Key.NumPad8] = 8,
    };

    // The real app shell (Phase 05). Composed from the DI-injected
    // MainWindowViewModel, which owns the sidebar/topbar/navigation.
    public MainWindow(
        MainWindowViewModel viewModel,
        ToastService toastService,
        DialogService dialogService)
    {
        InitializeComponent();
        DataContext = viewModel;
        Toast.Attach(toastService);
        dialogService.Attach(Modal, Drawer, Sheet);

        // Global hotkeys are handled by tunneling so they fire regardless of
        // which descendant currently holds focus.
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Ctrl+K opens the palette from anywhere — even while typing.
        if (ctrl && e.Key == Key.K)
        {
            e.Handled = true;
            vm.OpenPaletteCommand.Execute(null);
            return;
        }

        // Ctrl+Shift+D returns to the Dashboard.
        if (ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.D)
        {
            e.Handled = true;
            vm.NavigateHomeCommand.Execute(null);
            return;
        }

        // Alt+1..8 switch sections — but never hijack typing in a text field.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && AltSections.TryGetValue(e.Key, out var section)
            && !IsTextInputFocused())
        {
            e.Handled = true;
            vm.NavigateToSectionCommand.Execute(section);
        }
    }

    // True when the currently focused element is a text input, so Alt+digit
    // does not swallow a keystroke the user is typing.
    private bool IsTextInputFocused()
    {
        var focused = FocusManager?.GetFocusedElement() as Visual;
        return focused is TextBox || focused?.FindAncestorOfType<TextBox>() is not null;
    }
}
