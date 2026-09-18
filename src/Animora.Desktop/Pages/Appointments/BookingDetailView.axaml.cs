using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Appointment detail drawer (the reference's <c>.dtl</c> side panel). Opened
/// from the calendar for an existing event; its «ویرایش» action hands off to
/// the booking panel in edit mode.
/// </summary>
public partial class BookingDetailView : UserControl
{
    public BookingDetailView() => InitializeComponent();
}
