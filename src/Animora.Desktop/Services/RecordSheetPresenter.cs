using Animora.Desktop.Pages.Patients;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Services;

/// <summary>
/// Resolves the record page ViewModel from DI, loads the requested record, then
/// presents it in the centered <see cref="Controls.SheetHost"/> via
/// <see cref="IDialogService.ShowSheet"/>. Sizes are transcribed from the
/// reference: owner file 680×760 (<c>.owner-sheet</c>), patient file 1120×880
/// (<c>.pet-sheet</c>). Registered as a singleton; the ViewModels are transient
/// so each opened record is a fresh, fully-loaded instance.
/// </summary>
public sealed class RecordSheetPresenter : IRecordSheetPresenter
{
    private readonly IServiceProvider _services;
    private readonly IDialogService _dialogs;

    public RecordSheetPresenter(IServiceProvider services, IDialogService dialogs)
    {
        _services = services;
        _dialogs = dialogs;
    }

    public void OpenOwnerSheet(string ownerId)
    {
        var viewModel = _services.GetRequiredService<OwnerSheetViewModel>();
        viewModel.Load(ownerId);
        _dialogs.ShowSheet(viewModel, width: 680, height: 760);
    }

    public void OpenPetSheet(string petId)
    {
        var viewModel = _services.GetRequiredService<PetFileViewModel>();
        viewModel.Load(petId);
        _dialogs.ShowSheet(viewModel, width: 1120, height: 880);
    }
}
