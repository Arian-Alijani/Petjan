using Avalonia.Controls;

namespace Animora.Desktop.Pages.Boarding;

public partial class BoardingHistoryTabView : UserControl
{
    // History search is two-way bound to BoardingPageViewModel.HistoryQueryText;
    // debouncing now lives in the ViewModel (OnHistoryQueryTextChanged).
    public BoardingHistoryTabView() => InitializeComponent();
}
