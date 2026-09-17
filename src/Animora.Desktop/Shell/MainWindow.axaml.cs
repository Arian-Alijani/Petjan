using Animora.Desktop.Services;
using Avalonia.Controls;

namespace Animora.Desktop.Shell;

public partial class MainWindow : Window
{
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
        dialogService.Attach(Modal, Drawer);
    }
}
