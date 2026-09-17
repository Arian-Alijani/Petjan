using Avalonia.Controls;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// Shared pet-fields block (species picker + the four field rows + gender pills
/// + neutered switch). Reused by the add-owner wizard's step 2 and the add-pet
/// modal; the View is DataContext-driven from the hosting <see cref="PetFormViewModel"/>.
/// </summary>
public partial class PetFormView : UserControl
{
    public PetFormView() => InitializeComponent();
}
