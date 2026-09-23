using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Domain.Clinical;

/// <summary>What kind of clinical event a history entry records.</summary>
public enum MedicalHistoryKind
{
    /// <summary>An encounter.</summary>
    Visit = 0,

    /// <summary>A vaccine dose.</summary>
    Vaccination = 1,

    /// <summary>A surgical procedure.</summary>
    Surgery = 2,

    /// <summary>A laboratory result.</summary>
    Lab = 3,

    /// <summary>A weight measurement.</summary>
    Weight = 4,

    /// <summary>A prescription.</summary>
    Prescription = 5,

    /// <summary>A boarding stay.</summary>
    Boarding = 6,

    /// <summary>A free-text note.</summary>
    Note = 7,
}

/// <summary>
/// One line of a patient's medical history — the patient's permanent clinical record.
/// </summary>
/// <remarks>
/// <b>Append-only by construction</b> (<c>ARCHITECTURE.md</c> §8.5: medical history is preserved in
/// full and never overwritten). The type deliberately has no mutator of any kind and no
/// business-level soft delete: a correction is a <i>new</i> entry that references the same source,
/// and a sync conflict on this type can only ever add rows, never replace one. Entries are written
/// by the domain-event consumers wired in A10, not by screens.
/// </remarks>
public sealed class MedicalHistoryEntry : SyncableEntity
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private MedicalHistoryEntry()
    {
    }

    /// <summary>Creates a history entry. The only way to produce one.</summary>
    public MedicalHistoryEntry(
        string tenantId,
        string petId,
        MedicalHistoryKind kind,
        string title,
        DateTimeOffset occurredUtc,
        string recordedByUserId,
        IClock clock,
        string? detail = null,
        string? sourceEntityId = null,
        string? sourceEntityType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedByUserId);
        ArgumentNullException.ThrowIfNull(clock);

        Id = IdGenerator.NewId(clock.UtcNow);
        TenantId = tenantId;
        PetId = petId;
        Kind = kind;
        Title = title.Trim();
        OccurredUtc = occurredUtc;
        RecordedByUserId = recordedByUserId;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        SourceEntityId = sourceEntityId;
        SourceEntityType = sourceEntityType;
    }

    /// <summary>The patient this entry belongs to.</summary>
    public string PetId { get; private init; } = string.Empty;

    /// <summary>Event category.</summary>
    public MedicalHistoryKind Kind { get; private init; }

    /// <summary>One-line summary shown on the timeline.</summary>
    public string Title { get; private init; } = string.Empty;

    /// <summary>Longer description, when there is one.</summary>
    public string? Detail { get; private init; }

    /// <summary>When the clinical event happened, UTC — not when the row was written.</summary>
    public DateTimeOffset OccurredUtc { get; private init; }

    /// <summary>Id of the aggregate that caused this entry (visit, vaccination, …).</summary>
    public string? SourceEntityId { get; private init; }

    /// <summary>Type name of that aggregate, so the timeline can deep-link.</summary>
    public string? SourceEntityType { get; private init; }

    /// <summary>The staff member the entry is attributed to.</summary>
    public string RecordedByUserId { get; private init; } = string.Empty;
}
