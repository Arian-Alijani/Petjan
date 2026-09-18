using Avalonia.Controls;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>
/// The cheque details drawer panel (the reference's <c>.ch-drawer</c> scaffold
/// from Animora-UI/assets/cheques-ui.js <c>openCheque</c>). Purely
/// presentational — it hosts <see cref="ChequeDetailViewModel"/> and is shown
/// through <c>IDialogService.ShowDrawer</c>; all behaviour lives in the ViewModel.
/// </summary>
public partial class ChequeDetailView : UserControl
{
    public ChequeDetailView()
    {
        InitializeComponent();
    }
}
