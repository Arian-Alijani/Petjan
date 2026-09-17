using Avalonia.Controls;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// The add-owner/add-pet wizard modal (reference #ownerModal/.modal.wide). This
/// is the View <c>ViewLocator</c> was missing, so the wizard previously opened
/// an empty box.
/// </summary>
public partial class AddPatientWizardView : UserControl
{
    public AddPatientWizardView() => InitializeComponent();
}
