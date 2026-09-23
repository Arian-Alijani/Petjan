namespace Animora.Domain.Clinical;

/// <summary>
/// One prescribed drug on a visit.
/// </summary>
/// <param name="LineId">ULID identifying the line inside its visit.</param>
/// <param name="ProductId">
/// Inventory product when the drug is dispensed from stock (A06); <see langword="null"/> when the
/// owner fills the prescription elsewhere — the clinic still records what was prescribed.
/// </param>
/// <param name="DrugName">Drug name as written on the prescription.</param>
/// <param name="Dosage">Dose per administration, e.g. <c>"250 mg"</c>.</param>
/// <param name="Frequency">Administration frequency, e.g. <c>"BID"</c>.</param>
/// <param name="DurationDays">Length of the course in days.</param>
/// <param name="Instructions">Free-text instructions for the owner.</param>
public sealed record PrescriptionLine(
    string LineId,
    string? ProductId,
    string DrugName,
    string Dosage,
    string Frequency,
    int DurationDays,
    string? Instructions);
