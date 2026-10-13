using System.Security.Cryptography;

namespace Animora.Domain.Primitives;

/// <summary>
/// The single identifier generator of the product (CONVENTIONS.md §4). Produces a
/// 26-character Crockford base32 ULID: a 48-bit millisecond timestamp (10 chars)
/// followed by 80 bits of randomness (16 chars).
/// </summary>
/// <remarks>
/// <para>
/// Ids are lexicographically sortable: the Crockford alphabet is ASCII-ascending, so
/// ordinal string comparison equals numeric comparison, which keeps index locality on
/// both stores. Within the same millisecond the random component is incremented under a
/// lock, so two ids produced back to back are strictly increasing.
/// </para>
/// <para>
/// A <c>Guid</c> is never used as an id in business code. Wall-clock time here is an id
/// prefix only — never a business sequence or licensing authority (A-RULE 011).
/// </para>
/// </remarks>
public static class IdGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int IdLength = 26;
    private const int TimestampChars = 10;
    private const int RandomBytes = 10;
    private const long MaxTimestampMs = (1L << 48) - 1;

    private static readonly Lock Gate = new();
    private static readonly byte[] LastRandom = new byte[RandomBytes];
    private static long _lastMs = -1;

    // The explicit-instant overload keeps its own high-water state so a caller-supplied
    // (possibly past) instant can never reset the parameterless path's monotonic state.
    private static readonly byte[] LastRandomExplicit = new byte[RandomBytes];
    private static long _lastExplicitMs = -1;

    /// <summary>
    /// Creates a new ULID stamped with the current UTC time. Strictly greater than every
    /// id this process produced before it, even if the OS clock jumps backwards.
    /// </summary>
    public static string NewId() => Create(TimeProvider.System.GetUtcNow(), enforceMonotonic: true);

    /// <summary>
    /// Creates a new ULID stamped with an explicit instant. Intended for tests and for
    /// callers that already hold a clock reading; production code uses the injected
    /// <c>IClock</c> value or the parameterless overload. An explicitly supplied earlier
    /// instant is honoured as given — the caller, not this generator, owns that ordering.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The instant is outside the 48-bit ULID range.</exception>
    public static string NewId(DateTimeOffset at) => Create(at, enforceMonotonic: false);

    /// <summary>
    /// Deterministic id for the <paramref name="slot"/>-th aggregate a command creates, derived from the command's
    /// <paramref name="operationId"/>. The offline client and the server run the <b>same</b> handler for the same operation, so deriving
    /// (rather than minting) ids makes both sides create the <b>same</b> aggregate ids: follow-up commands that address a just-created
    /// aggregate by id (update owner, add invoice line, post …) resolve on the server, and the replicated row never duplicates the
    /// local one. Slot 0 is the operation id itself; slot n keeps its 48-bit timestamp prefix and takes 80 bits of
    /// <c>SHA-256(operationId ‖ ':' ‖ n)</c> as randomness. Falls back to a fresh ULID (<paramref name="at"/>) when the operation id is
    /// absent or not ULID-shaped, so ad-hoc callers/tests are unaffected.
    /// </summary>
    /// <param name="operationId">The creating command's operation id.</param>
    /// <param name="slot">Creation slot within the command (0 = primary aggregate).</param>
    /// <param name="at">Timestamp for the non-deterministic fallback.</param>
    public static string ForOperation(string? operationId, int slot, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        if (!LooksLikeUlid(operationId)) return NewId(at);
        if (slot == 0) return operationId!;
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operationId + ":" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture)), hash);
        Span<char> buffer = stackalloc char[IdLength];
        operationId.AsSpan(0, TimestampChars).CopyTo(buffer);
        EncodeGroup(hash[..5], buffer.Slice(TimestampChars, 8));
        EncodeGroup(hash[5..10], buffer.Slice(TimestampChars + 8, 8));
        return new string(buffer);
    }

    /// <summary>
    /// Deterministic <paramref name="length"/>-char code suffix for a human-visible code (SKU/category/supplier code) a command mints.
    /// Uses the tail of the operation id so client and server agree; falls back to a random suffix when the operation id is absent/not ULID-shaped.
    /// </summary>
    public static string CodeSuffix(string? operationId, int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 16);
        return LooksLikeUlid(operationId)
            ? operationId![^length..]
            : Guid.NewGuid().ToString("N")[..length].ToUpperInvariant();
    }

    private static bool LooksLikeUlid(string? value)
    {
        if (value is null || value.Length != IdLength || value[0] > '7') return false;
        foreach (var c in value)
            if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }

    private static string Create(DateTimeOffset at, bool enforceMonotonic)
    {
        var ms = at.ToUnixTimeMilliseconds();
        ArgumentOutOfRangeException.ThrowIfNegative(ms);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ms, MaxTimestampMs);

        Span<byte> random = stackalloc byte[RandomBytes];
        lock (Gate)
        {
            var lastRandom = enforceMonotonic ? LastRandom : LastRandomExplicit;
            var lastMs = enforceMonotonic ? _lastMs : _lastExplicitMs;

            if (enforceMonotonic && ms < lastMs)
            {
                ms = lastMs;
            }

            if (ms == lastMs)
            {
                IncrementInPlace(lastRandom);
            }
            else
            {
                RandomNumberGenerator.Fill(lastRandom);
                if (enforceMonotonic)
                {
                    _lastMs = ms;
                }
                else
                {
                    _lastExplicitMs = ms;
                }
            }

            lastRandom.CopyTo(random);
        }

        return Encode(ms, random);
    }

    /// <summary>Big-endian +1 over the 80-bit random component.</summary>
    private static void IncrementInPlace(byte[] buffer)
    {
        for (var i = buffer.Length - 1; i >= 0; i--)
        {
            if (++buffer[i] != 0)
            {
                return;
            }
        }

        // 2^80 ids inside one millisecond is not reachable; reaching it is a defect.
        throw new InvalidOperationException("ULID randomness space exhausted within a single millisecond.");
    }

    private static string Encode(long timestampMs, ReadOnlySpan<byte> random)
    {
        Span<char> buffer = stackalloc char[IdLength];

        var ms = timestampMs;
        for (var i = TimestampChars - 1; i >= 0; i--)
        {
            buffer[i] = Alphabet[(int)(ms & 31)];
            ms >>= 5;
        }

        // 80 random bits = two 40-bit groups, each encoded as 8 base32 chars.
        EncodeGroup(random[..5], buffer.Slice(TimestampChars, 8));
        EncodeGroup(random[5..], buffer.Slice(TimestampChars + 8, 8));

        return new string(buffer);
    }

    private static void EncodeGroup(ReadOnlySpan<byte> fiveBytes, Span<char> eightChars)
    {
        long value = 0;
        foreach (var b in fiveBytes)
        {
            value = (value << 8) | b;
        }

        for (var i = 7; i >= 0; i--)
        {
            eightChars[i] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }
    }
}
