using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Booking/detail side panel (mirrors the reference's booking sheet in
/// Animora-UI/assets/appointments-panel.js). Opened as a drawer from the
/// calendar's <c>OpenBooking</c> command; the search input debounces its query
/// into the ViewModel exactly like the reference's <c>input</c> listener.
/// </summary>
public partial class BookingPanelView : UserControl
{
    private DispatcherTimer? _queryTimer;

    public BookingPanelView() => InitializeComponent();

    private void OnQueryChanged(object? sender, TextChangedEventArgs e)
    {
        var text = (sender as TextBox)?.Text ?? string.Empty;
        _queryTimer?.Stop();
        _queryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _queryTimer.Tick += (_, _) =>
        {
            _queryTimer!.Stop();
            if (DataContext is BookingPanelViewModel vm)
            {
                vm.QueryText = text;
            }
        };
        _queryTimer.Start();
    }
}
