using Animora.Desktop.Shell;
using Avalonia.Controls;

namespace Animora.Desktop.Pages.Login;

/// <summary>
/// Startup sign-in window. Built from the DI-injected <see cref="LoginViewModel"/>;
/// App swaps in the main shell once <see cref="LoginViewModel.LoginSucceeded"/>
/// fires. Views are otherwise ViewModel-agnostic — all state lives in the VM.
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        // Always open (and re-reveal) centered on the active monitor; see WindowCentering.
        WindowCentering.Enable(this);
    }
}
