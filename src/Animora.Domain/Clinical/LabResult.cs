namespace Animora.Domain.Clinical;

/// <summary>
/// One laboratory result sheet recorded on a visit: a date, a free-text description and the measured
/// values per test type. Immutable snapshot stored in the visit's JSON <c>LabResults</c> column.
/// </summary>
/// <param name="Id">ULID identifying the record inside its visit.</param>
/// <param name="TakenUtc">When the sample / test was taken (the date the doctor picked), UTC.</param>
/// <param name="Description">Free-text notes; <see langword="null"/> when none.</param>
/// <param name="Items">The measured values, one row per test type.</param>
public sealed record LabResult(
    string Id,
    DateTimeOffset TakenUtc,
    string? Description,
    IReadOnlyList<LabResultItem> Items);

/// <summary>One measured value of a <see cref="LabResult"/>.</summary>
/// <param name="TypeId">Id of the configured lab-test type; denormalised with <paramref name="TypeName"/> so the record stays readable if the type is later removed.</param>
/// <param name="TypeName">Name of the test type at recording time.</param>
/// <param name="Value">The measured value as typed by the doctor.</param>
/// <param name="Unit">Measurement unit; <see langword="null"/> when none.</param>
public sealed record LabResultItem(string TypeId, string TypeName, string Value, string? Unit);
