using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Domain.Patients;

/// <summary>Gender recorded for an owner; optional by design.</summary>
public enum OwnerGender
{
    /// <summary>Not recorded.</summary>
    Unspecified = 0,

    /// <summary>Male.</summary>
    Male = 1,

    /// <summary>Female.</summary>
    Female = 2,
}

/// <summary>
/// The human client of the clinic: the party pets belong to and invoices are issued to.
/// </summary>
/// <remarks>
/// Demographic fields are an <b>explicit merge</b> on conflict (<c>ARCHITECTURE.md</c> §8.5) — this
/// aggregate therefore exposes one deliberate <see cref="UpdateContactDetails"/> mutator rather than
/// free-floating setters, so a merge always has a single funnel to go through.
/// </remarks>
public sealed class Owner : AggregateRoot
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Owner()
    {
    }

    /// <summary>Full name as the clinic knows the client.</summary>
    public string FullName { get; private set; } = string.Empty;

    /// <summary>Primary contact number, normalized by <see cref="NormalizePhone"/>.</summary>
    public string PhonePrimary { get; private set; } = string.Empty;

    /// <summary>Optional secondary number, normalized the same way.</summary>
    public string? PhoneSecondary { get; private set; }

    /// <summary>Optional landline number, normalized by <see cref="NormalizePhone"/>.</summary>
    public string? PhoneLandline { get; private set; }

    /// <summary>Optional free-form "how the client found the clinic" referral note.</summary>
    public string? Referral { get; private set; }

    /// <summary>Iranian national id, when the client supplied one.</summary>
    public string? NationalId { get; private set; }

    /// <summary>Optional e-mail address.</summary>
    public string? Email { get; private set; }

    /// <summary>Free-form postal address.</summary>
    public string? Address { get; private set; }

    /// <summary>City, kept separate from <see cref="Address"/> for filtering.</summary>
    public string? City { get; private set; }

    /// <summary>Recorded gender.</summary>
    public OwnerGender Gender { get; private set; } = OwnerGender.Unspecified;

    /// <summary>Reception notes about the client (not clinical notes — those belong to the pet).</summary>
    public string? Notes { get; private set; }

    /// <summary>When the client was first registered, UTC.</summary>
    public DateTimeOffset RegisteredUtc { get; private set; }

    /// <summary><see langword="true"/> when the clinic refuses further business with this client.</summary>
    public bool IsBlocked { get; private set; }

    /// <summary>Why the client was blocked; kept for the audit trail.</summary>
    public string? BlockReason { get; private set; }

    /// <summary>Persisted, normalized search index maintained by the persistence layer's ISearchKeyProvider
    /// on every write (C01). Derived data — the domain never reads it and a merge never writes it.</summary>
    public string SearchKey { get; set; } = string.Empty;

    /// <summary>Who soft-deleted this owner; replicated so peers can show the actor. <see langword="null"/> while live.</summary>
    public string? DeletedByUserId { get; private set; }

    /// <summary>When the owner was soft-deleted, UTC; replicated so peers show the same instant. <see langword="null"/> while live.</summary>
    public DateTimeOffset? DeletedUtc { get; private set; }

    /// <summary>
    /// Soft-deletes the owner and records the actor + instant (the audit trail that replicates to peers).
    /// Idempotent: an already-deleted owner keeps its original actor/time.
    /// </summary>
    public void MarkDeleted(string userId, DateTimeOffset whenUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (Deleted)
        {
            return;
        }

        Deleted = true;
        DeletedByUserId = userId;
        DeletedUtc = whenUtc;
    }

    /// <summary>Registers a new, unblocked owner. <paramref name="id"/> is the deterministic id derived from the creating command's
    /// operation (<see cref="IdGenerator.ForOperation"/>) so client and server agree; <see langword="null"/> mints a fresh ULID.
    /// <paramref name="phonePrimary"/> may be absent for an imported record whose source has no contact number — see
    /// <see cref="NormalizePrimaryPhone"/>.</summary>
    public static Owner Register(
        string tenantId,
        string fullName,
        string? phonePrimary,
        IClock clock,
        OwnerGender gender = OwnerGender.Unspecified,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        return new Owner
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(now) : id,
            TenantId = tenantId,
            FullName = fullName.Trim(),
            PhonePrimary = NormalizePrimaryPhone(phonePrimary),
            Gender = gender,
            RegisteredUtc = now,
        };
    }

    /// <summary>
    /// Replaces the contact/demographic block as one unit — the single funnel an explicit
    /// conflict merge writes through (<c>ARCHITECTURE.md</c> §8.5).
    /// </summary>
    public void UpdateContactDetails(
        string fullName,
        string? phonePrimary,
        string? phoneSecondary = null,
        string? nationalId = null,
        string? email = null,
        string? address = null,
        string? city = null,
        OwnerGender? gender = null,
        string? notes = null,
        string? phoneLandline = null,
        string? referral = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        FullName = fullName.Trim();
        PhonePrimary = NormalizePrimaryPhone(phonePrimary);
        PhoneSecondary = string.IsNullOrWhiteSpace(phoneSecondary) ? null : NormalizePhone(phoneSecondary);
        // A landline is normalized to digits like the mobiles; a digitless value folds to "not recorded".
        PhoneLandline = string.IsNullOrWhiteSpace(phoneLandline) || NormalizePhone(phoneLandline).Length == 0
            ? null
            : NormalizePhone(phoneLandline);
        NationalId = Clean(nationalId);
        Email = Clean(email);
        Address = Clean(address);
        City = Clean(city);
        Gender = gender ?? Gender;
        Notes = Clean(notes);
        Referral = Clean(referral);
    }

    /// <summary>Blocks the client. Idempotent: the original reason is kept.</summary>
    public void Block(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (IsBlocked)
        {
            return;
        }

        IsBlocked = true;
        BlockReason = reason.Trim();
    }

    /// <summary>Lifts a block and clears its reason.</summary>
    public void Unblock()
    {
        IsBlocked = false;
        BlockReason = null;
    }

    /// <summary>
    /// Reduces a phone number to digits only, preserving a leading <c>0</c>. Persian
    /// (U+06F0–U+06F9) and Arabic-Indic (U+0660–U+0669) digits are folded to ASCII first, so
    /// numbers typed on a Persian keyboard compare equal to the same number typed on a Latin one.
    /// Separators, spaces and a leading <c>+</c> are dropped.
    /// </summary>
    public static string NormalizePhone(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var buffer = new char[raw.Length];
        var length = 0;
        foreach (var c in raw)
        {
            var ascii = c switch
            {
                >= '0' and <= '9' => c,
                >= '\u06F0' and <= '\u06F9' => (char)(c - '\u06F0' + '0'),
                >= '\u0660' and <= '\u0669' => (char)(c - '\u0660' + '0'),
                _ => '\0',
            };

            if (ascii != '\0')
            {
                buffer[length++] = ascii;
            }
        }

        return new string(buffer, 0, length);
    }

    /// <summary>
    /// Normalizes the primary phone and refuses a value that cannot be a phone number.
    /// <para>
    /// A NON-BLANK but digitless value (<c>"---"</c>, <c>"abc"</c>, <c>"+"</c>) normalizes to the empty string, and
    /// silently storing that was the defect: the row reached the server, and the replica consumer — which requires
    /// <c>PhonePrimary</c> — then dropped the whole Owner, and with it every pet, visit and invoice hanging off it, on
    /// every peer. Such a value is a data-entry error, so it is refused here (and, for operator-facing commands,
    /// already reported as a validation failure by <c>Rules.Phone</c>).
    /// </para>
    /// <para>
    /// An ABSENT phone (<see langword="null"/>/blank) is different: "contact number not recorded" is a real state for
    /// a legacy record whose source row has no phone, and it is stored as the empty string. Only a source import
    /// produces it — every operator-facing command requires a usable number.
    /// </para>
    /// </summary>
    private static string NormalizePrimaryPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = NormalizePhone(raw);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A primary phone number must contain at least one digit.", nameof(PhonePrimary));
        }

        return normalized;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
