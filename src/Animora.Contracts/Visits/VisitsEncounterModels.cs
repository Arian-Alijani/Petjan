namespace Animora.Contracts.Visits;

/// <summary>
/// Discriminates a catalog row — a billable <em>service</em> («خدمت») from a
/// physical <em>item</em> («کالا») — mirroring the reference's
/// <c>kind: 'svc' | 'item'</c> tags on <c>CLINIC.SERVICES</c>/<c>CLINIC.ITEMS</c>
/// (Animora-UI/assets/clinic-data.js) which drive <c>rxLineHtml</c>'s badge.
/// </summary>
public enum RxCatalogKind
{
    Service,
    Item,
}

/// <summary>
/// One selectable row of the encounter's billable catalog, mirroring the
/// <c>{ id, title, price }</c> shape the reference's add-dropdowns render
/// (<c>C().SERVICES</c>/<c>C().ITEMS</c>). <see cref="Price"/> is in Rial.
/// </summary>
public sealed record RxCatalogItem(
    RxCatalogKind Kind,
    string Id,
    string Title,
    long Price,
    long? OnHandScaled = null);

/// <summary>
/// One line in an encounter's billable list — the shape <c>rxLineHtml</c>
/// renders from the reference's <c>S.enc.services</c>/<c>items</c> entries
/// (<c>{ sv|it, qty, note }</c>) once the catalog row has been resolved. The
/// <see cref="Quantity"/> is settable because the reference mutates the qty
/// in place (its <c>.qty</c> input writes straight into the encounter draft);
/// the running subtotal always sums <see cref="LineTotal"/>
/// (<c>computeSubtotal</c>).
/// </summary>
public sealed class RxLine
{
    public RxLine(
        string id,
        RxCatalogKind kind,
        string catalogId,
        string title,
        long unitPrice,
        int quantity,
        string note)
    {
        Id = id;
        Kind = kind;
        CatalogId = catalogId;
        Title = title;
        UnitPrice = unitPrice;
        Quantity = quantity;
        Note = note;
    }

    public string Id { get; }

    public RxCatalogKind Kind { get; }

    public string CatalogId { get; }

    public string Title { get; }

    public long UnitPrice { get; }

    public int Quantity { get; set; }

    public string Note { get; }

    /// <summary>Line total in Rial — <c>price × qty</c> (reference <c>sum</c>).</summary>
    public long LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// The four vital-sign readings the reference's <c>renderVitals</c> step
/// captures (weight, temperature, pulse, respiration). <see langword="null"/> means "not
/// recorded" (distinct from an invalid entry, which the wizard rejects before it gets here).
/// </summary>
public sealed record Vitals(
    double? WeightKg,
    double? TemperatureCelsius,
    int? PulseBpm,
    int? RespirationRate)
{
    public static Vitals Empty { get; } = new(null, null, null, null);
}

/// <summary>
/// The result of completing an encounter, mirroring what the reference's
/// success screen (<c>renderDone</c>) shows: the patient, the doctor and the
/// invoice total derived from <c>computeSubtotal</c> + a 9% tax line.
/// </summary>
public sealed record EncounterSummary(
    string EncounterId,
    string PatientName,
    string PatientKind,
    string DoctorId,
    string DoctorShortName,
    long Subtotal,
    long Tax,
    long Total,
    DateTime CompletedAt);

/// <summary>
/// A file attached to a radiology study. The bytes live in the content-addressed local file store;
/// <see cref="Hash"/> (lowercase hex SHA-256) is its key.
/// </summary>
public sealed record RadiologyFile(string Hash, string Name, string ContentType);

/// <summary>
/// One radiology study recorded on the encounter («رادیولوژی»): the date it was taken, the imaged
/// organ / body part, the written result and the attached image files. <see cref="Id"/> is empty for
/// an entry that has not been saved yet — the service mints the id and returns it from
/// <see cref="IVisitEncounterService.AddRadiologyAsync"/>.
/// </summary>
public sealed record RadiologyEntry(
    string Id,
    DateTime TakenAt,
    string BodyPart,
    string? Result,
    IReadOnlyList<RadiologyFile> Files);

/// <summary>
/// One measured value of a lab result sheet: the configured test type (id + name as recorded, so history survives
/// the type's later removal), the value the doctor typed and its unit.
/// </summary>
public sealed record LabItem(string TypeId, string TypeName, string Value, string? Unit);

/// <summary>
/// One laboratory result sheet recorded on the encounter («آزمایشات»): the date, a free-text description and the
/// per-test values. <see cref="Id"/> is empty for an unsaved entry — the service mints the id.
/// </summary>
public sealed record LabEntry(
    string Id,
    DateTime TakenAt,
    string? Description,
    IReadOnlyList<LabItem> Items);

/// <summary>
/// Complete, read-only snapshot of one visit for the visit viewer sheet: identity and ownership,
/// the attending doctor, lifecycle state and clinic-local times, triage vitals (including the
/// membrane/hydration notes the encounter wizard records), the four tagged clinical note sections,
/// billable lines and the paraclinic records. Every string is raw data — Persian labels and
/// formatting belong to the Desktop layer.
/// </summary>
public sealed record VisitDetail(
    string Id,
    string PatientName,
    string PatientKind,
    string OwnerName,
    string OwnerPhone,
    string DoctorName,
    string DoctorSpecialty,
    string Reason,
    string State,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string ChiefComplaint,
    double? WeightKg,
    double? TemperatureC,
    int? PulseBpm,
    int? RespirationRate,
    string? MucousMembrane,
    string? Hydration,
    string History,
    string Exam,
    string Diagnosis,
    string Plan,
    IReadOnlyList<VisitDetailLine> Lines,
    IReadOnlyList<VisitDetailRadiology> Radiology,
    IReadOnlyList<VisitDetailLab> Labs,
    long Subtotal,
    string? InvoiceId);

/// <summary>One billable line of a <see cref="VisitDetail"/>: title, quantity, unit price and line total (Rial).</summary>
public sealed record VisitDetailLine(string Title, decimal Quantity, long UnitPrice, long LineTotal);

/// <summary>One radiology study of a <see cref="VisitDetail"/>; <see cref="AttachmentCount"/> is the stored file count.</summary>
public sealed record VisitDetailRadiology(
    string Id,
    DateTime TakenAt,
    string BodyPart,
    string Result,
    int AttachmentCount);

/// <summary>One laboratory result sheet of a <see cref="VisitDetail"/> with its measured values.</summary>
public sealed record VisitDetailLab(
    string Id,
    DateTime TakenAt,
    string Description,
    IReadOnlyList<VisitDetailLabItem> Items);

/// <summary>One measured value of a <see cref="VisitDetailLab"/>.</summary>
public sealed record VisitDetailLabItem(string Name, string Value, string? Unit);
