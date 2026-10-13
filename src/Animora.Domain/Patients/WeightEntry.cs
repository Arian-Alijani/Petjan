using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Domain.Patients;

/// <summary>
/// One weight measurement of a patient. <b>Append-only</b>: there is no mutator, because a weight
/// curve is clinical evidence. A mistyped reading is corrected by recording a new entry that points
/// at the wrong one through <see cref="CorrectsEntryId"/>.
/// </summary>
public sealed class WeightEntry : SyncableEntity
{
    /// <summary>Lowest accepted reading, kilograms (exclusive lower bound is zero).</summary>
    public const decimal MinWeightKg = 0m;

    /// <summary>Highest accepted reading, kilograms — above this it is a data-entry error.</summary>
    public const decimal MaxWeightKg = 300m;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private WeightEntry()
    {
    }

    /// <summary>The measured patient.</summary>
    public string PetId { get; private init; } = string.Empty;

    /// <summary>The reading in kilograms; always &gt; 0 and ≤ <see cref="MaxWeightKg"/>.</summary>
    public decimal WeightKg { get; private init; }

    /// <summary>When the measurement was taken, UTC.</summary>
    public DateTimeOffset MeasuredUtc { get; private init; }

    /// <summary>Optional remark (scale used, fasting state, …).</summary>
    public string? Note { get; private init; }

    /// <summary>The staff member who recorded the reading.</summary>
    public string RecordedByUserId { get; private init; } = string.Empty;

    /// <summary>
    /// The earlier entry this one supersedes; <see langword="null"/> for an original measurement.
    /// The superseded row is kept — nothing in the weight history is ever rewritten.
    /// </summary>
    public string? CorrectsEntryId { get; private init; }

    /// <summary>
    /// Records a measurement. An out-of-range reading is expected user error, so it returns
    /// <c>WEIGHT_OUT_OF_RANGE</c> rather than throwing (CONVENTIONS.md §4).
    /// </summary>
    public static Result<WeightEntry> Record(
        string tenantId,
        string petId,
        decimal weightKg,
        DateTimeOffset measuredUtc,
        string recordedByUserId,
        IClock clock,
        string? note = null,
        string? correctsEntryId = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedByUserId);
        ArgumentNullException.ThrowIfNull(clock);

        if (weightKg <= MinWeightKg || weightKg > MaxWeightKg)
        {
            return Result<WeightEntry>.Fail(
                ErrorCodes.WeightOutOfRange,
                $"WeightKg must be in ({MinWeightKg}, {MaxWeightKg}]; got {weightKg}.");
        }

        return Result<WeightEntry>.Ok(new WeightEntry
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(clock.UtcNow) : id,
            TenantId = tenantId,
            PetId = petId,
            WeightKg = weightKg,
            MeasuredUtc = measuredUtc,
            RecordedByUserId = recordedByUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CorrectsEntryId = correctsEntryId,
        });
    }
}
