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

    /// <summary>
    /// The read-only viewer snapshot of one visit. Times come out local (the clinic's own clock),
    /// every optional section stays <see langword="null"/> or empty so the viewer can hide it.
    /// </summary>
    public static VisitDetail ToContract(this EncounterEditReadModel row)
    {
        var draft = row.Draft;
        var vitals = row.Vitals;
        var lines = row.Lines.Select(l => new VisitDetailLine(l.Title, l.Quantity.Value,
            l.UnitPrice.Minor, (long)(l.Quantity.Value * l.UnitPrice.Minor))).ToArray();
        var subtotal = (long)lines.Sum(l => l.LineTotal);
        return new VisitDetail(
            draft.VisitId,
            draft.PetName,
            draft.PetKind,
            draft.OwnerName,
            draft.OwnerPhone,
            draft.DoctorName,
            draft.DoctorSpecialty,
            row.Reason.ToString(),
            row.State.ToString(),
            row.StartedUtc.LocalDateTime,
            row.CompletedUtc?.LocalDateTime,
            row.ChiefComplaint ?? string.Empty,
            vitals?.WeightKg is { } w ? (double?)w : null,
            vitals?.TemperatureC is { } t ? (double?)t : null,
            vitals?.HeartRateBpm,
            vitals?.RespiratoryRate,
            vitals?.MucousMembrane,
            vitals?.Hydration,
            row.History,
            row.Exam,
            row.Diagnosis,
            row.Plan,
            lines,
            row.Radiology.OrderByDescending(r => r.TakenUtc).Select(r => new VisitDetailRadiology(
                r.Id, r.TakenUtc.LocalDateTime, r.BodyPart, r.Result ?? string.Empty,
                r.Attachments.Count)).ToArray(),
            row.Labs.OrderByDescending(l => l.TakenUtc).Select(l => new VisitDetailLab(
                l.Id, l.TakenUtc.LocalDateTime, l.Description ?? string.Empty,
                l.Items.Select(i => new VisitDetailLabItem(i.TypeName, i.Value, i.Unit)).ToArray())).ToArray(),
            subtotal,
            row.InvoiceId);
    }

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
