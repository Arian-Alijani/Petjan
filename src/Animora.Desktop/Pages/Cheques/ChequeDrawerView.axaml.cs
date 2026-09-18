using Avalonia.Controls;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>
/// The add/edit cheque drawer panel (the reference's <c>.ch-modal</c> form).
/// Purely presentational — it hosts <see cref="ChequeDrawerViewModel"/> and is
/// shown through <c>IDialogService.ShowModal</c>; all behaviour lives in the
/// ViewModel.
/// </summary>
public partial class ChequeDrawerView : UserControl
{
    public ChequeDrawerView()
    {
        InitializeComponent();
    }
}
