using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments.Export;

/// <summary>
/// The appointments «خروجی / چاپ» options dialog (shown through
/// <c>IDialogService.ShowModal</c>). All state lives in
/// <see cref="AppointmentExportViewModel"/>.
/// </summary>
public partial class AppointmentExportView : UserControl
{
    public AppointmentExportView() => InitializeComponent();
}
