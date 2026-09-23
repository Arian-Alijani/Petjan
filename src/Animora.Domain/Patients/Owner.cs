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

    /// <summary>Registers a new, unblocked owner.</summary>
    public static Owner Register(
        string tenantId,
        string fullName,
        string phonePrimary,
        IClock clock,
        OwnerGender gender = OwnerGender.Unspecified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phonePrimary);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        return new Owner
        {
            Id = IdGenerator.NewId(now),
            TenantId = tenantId,
            FullName = fullName.Trim(),
            PhonePrimary = NormalizePhone(phonePrimary),
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
        string phonePrimary,
        string? phoneSecondary = null,
        string? nationalId = null,
        string? email = null,
        string? address = null,
        string? city = null,
        OwnerGender? gender = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phonePrimary);

        FullName = fullName.Trim();
        PhonePrimary = NormalizePhone(phonePrimary);
        PhoneSecondary = string.IsNullOrWhiteSpace(phoneSecondary) ? null : NormalizePhone(phoneSecondary);
        NationalId = Clean(nationalId);
        Email = Clean(email);
        Address = Clean(address);
        City = Clean(city);
        Gender = gender ?? Gender;
        Notes = Clean(notes);
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

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
