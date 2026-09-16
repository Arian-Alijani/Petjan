using Avalonia.Controls;

namespace Animora.Desktop.Shell;

public partial class MainWindow : Window
{
    // The real app shell (Phase 05). Composed from the DI-injected
    // MainWindowViewModel, which owns the sidebar/topbar/navigation.
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
