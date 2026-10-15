using Animora.Application.UseCases.Visits;
using Animora.Contracts.Visits;

namespace Animora.Client.Services.Visits;

internal static class VisitEncounterMapping
{
    public static RxCatalogItem ToContract(this RxCatalogItemReadModel row) =>
        new(row.Kind == "Service" ? RxCatalogKind.Service : RxCatalogKind.Item,
            row.Id, row.Title, row.Price, row.OnHandScaled);

    public static EncounterSummary ToContract(this EncounterSummaryReadModel row) =>
        new(row.EncounterId, row.PatientName, row.PatientKind, row.DoctorId,
            row.DoctorShortName, row.Subtotal, row.Tax, row.Total, row.CompletedUtc.LocalDateTime);
}
