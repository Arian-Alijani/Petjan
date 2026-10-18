using Animora.Desktop.Diagnostics;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Pages.Visits;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Services;

/// <summary>
/// Resolves the record page ViewModel from DI, loads the requested record, then
/// presents it in the centered <see cref="Controls.SheetHost"/> via
/// <see cref="IDialogService.ShowSheet"/>. Sizes are transcribed from the
/// reference: owner file 680×760 (<c>.owner-sheet</c>), patient file 1120×880
/// (<c>.pet-sheet</c>). Registered as a singleton; the ViewModels are transient
/// so each opened record is a fresh, fully-loaded instance.
///
/// The load is awaited — never blocked on the UI thread (PERFORMANCE.md §4) — so
/// a Datasync/SQLite-backed <c>IPatientsQueryService</c> keeps the shell
/// responsive while the record sheet is being fetched.
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

    public async Task OpenOwnerSheetAsync(string ownerId)
    {
        AppTrace.Info($"OpenOwnerSheetAsync(ownerId={ownerId}) — resolving OwnerSheetViewModel and loading…", "patients");
        var viewModel = _services.GetRequiredService<OwnerSheetViewModel>();
        await viewModel.LoadAsync(ownerId);
        _dialogs.ShowSheet(viewModel, width: 1120, height: 880);
        AppTrace.Info($"owner sheet presented (ownerId={ownerId}).", "patients");
    }

    public async Task OpenPetSheetAsync(string petId)
    {
        AppTrace.Info($"OpenPetSheetAsync(petId={petId}) — resolving PetFileViewModel and loading…", "patients");
        var viewModel = _services.GetRequiredService<PetFileViewModel>();
        await viewModel.LoadAsync(petId);
        _dialogs.ShowSheet(viewModel, width: 1120, height: 880);
        AppTrace.Info($"pet sheet presented (petId={petId}).", "patients");
    }

    public async Task OpenVisitSheetAsync(string visitId)
    {
        AppTrace.Info($"OpenVisitSheetAsync(visitId={visitId}) — resolving VisitDetailViewModel and loading…", "visits");
        var viewModel = _services.GetRequiredService<VisitDetailViewModel>();
        await viewModel.LoadAsync(visitId);
        _dialogs.ShowSheet(viewModel, width: 980, height: 780);
        AppTrace.Info($"visit sheet presented (visitId={visitId}).", "visits");
    }
}
