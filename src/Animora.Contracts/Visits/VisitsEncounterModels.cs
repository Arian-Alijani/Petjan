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
