using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Booking/detail side panel (mirrors the reference's booking sheet in
/// Animora-UI/assets/appointments-panel.js). Opened as a drawer from the
/// calendar's <c>OpenBooking</c> command. The search input is two-way bound to
/// <see cref="BookingPanelViewModel.QueryText"/>; debouncing now lives in the
/// ViewModel (OnQueryTextChanged), so no per-keystroke timer is created here.
/// </summary>
public partial class BookingPanelView : UserControl
{
    public BookingPanelView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => (DataContext as BookingPanelViewModel)?.StopPatientSearch();
    }
}
