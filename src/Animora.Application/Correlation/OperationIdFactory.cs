using System.Security.Cryptography;
using System.Text;
using Animora.Domain.Primitives;

namespace Animora.Application.Correlation;

/// <summary>The single client-side source of <c>OperationId</c>s (A-RULE 005).</summary>
public sealed class OperationIdFactory
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32 (matches IdGenerator).

#pragma warning disable CA1822 // Instance member by design: injected, so tests and later phases can substitute the source.
    public string New() => IdGenerator.NewId();

    /// <summary>
    /// Produces a <b>stable</b> 26-char id from <paramref name="seed"/> so a retried command that
    /// derives its <c>OperationId</c> from the same seed replays idempotently (A-RULE 006). Pure —
    /// the same seed always yields the same id, on any machine, with no clock or randomness (C02;
    /// used for <c>"visit-start:" + ticketId</c>).
    /// </summary>
    public string Deterministic(string seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(seed), hash);

        // Fold the 256-bit hash into a 26-char Crockford base32 id, matching ULID width/shape so it
        // passes Rules.UlidFormat (the OperationId validator in SessionCurrentUserProvider). A real ULID
        // encodes 128 bits in 26 base32 chars, so the leading char carries only 3 bits and must be '0'..'7';
        // masking every other char with 0x1F while masking the first with 0x07 keeps the id deterministic
        // AND valid. The Alphabet already omits I/L/O/U, the other UlidFormat requirement.
        var chars = new char[26];
        for (var i = 0; i < 26; i++)
        {
            var mask = i == 0 ? 0x07 : 0x1F;
            chars[i] = Alphabet[hash[i % hash.Length] & mask];
        }

        return new string(chars);
    }
#pragma warning restore CA1822
}
