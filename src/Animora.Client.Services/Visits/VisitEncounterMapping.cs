using Animora.Application.UseCases.Visits;
using Animora.Contracts.Visits;
using Animora.Domain.Clinical;

namespace Animora.Client.Services.Visits;

internal static class VisitEncounterMapping
{
    public static RxCatalogItem ToContract(this RxCatalogItemReadModel row) =>
        new(row.Kind == "Service" ? RxCatalogKind.Service : RxCatalogKind.Item,
            row.Id, row.Title, row.Price, row.OnHandScaled);

    public static EncounterSummary ToContract(this EncounterSummaryReadModel row) =>
        new(row.EncounterId, row.PatientName, row.PatientKind, row.DoctorId,
            row.DoctorShortName, row.Subtotal, row.Tax, row.Total, row.CompletedUtc.LocalDateTime);

    public static RadiologyEntry ToContract(this RadiologyRecord row) =>
        new(row.Id, row.TakenUtc.LocalDateTime, row.BodyPart, row.Result,
            row.Attachments.Select(a => new RadiologyFile(a.FileHash, a.FileName, a.ContentType)).ToArray());

    public static AddRadiologyCommand ToCommand(this RadiologyEntry entry, string encounterId) =>
        new(encounterId, entry.TakenAt.ToDateTimeOffset(), entry.BodyPart, entry.Result,
            entry.Files.Select(f => new RadiologyAttachmentInput(f.Hash, f.Name, f.ContentType)).ToArray(),
            string.IsNullOrWhiteSpace(entry.Id) ? null : entry.Id);

    public static LabEntry ToContract(this LabResult row) =>
        new(row.Id, row.TakenUtc.LocalDateTime, row.Description,
            row.Items.Select(i => new LabItem(i.TypeId, i.TypeName, i.Value, i.Unit)).ToArray());

    public static AddLabResultCommand ToCommand(this LabEntry entry, string encounterId) =>
        new(encounterId, entry.TakenAt.ToDateTimeOffset(), entry.Description,
            entry.Items.Select(i => new LabResultItemInput(i.TypeId, i.TypeName, i.Value, i.Unit)).ToArray(),
            string.IsNullOrWhiteSpace(entry.Id) ? null : entry.Id);

    /// <summary>A picked calendar date/time (local, or unspecified from a date picker) as the absolute instant the backend stores.</summary>
    public static DateTimeOffset ToDateTimeOffset(this DateTime value) =>
        new(value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Local));
}
