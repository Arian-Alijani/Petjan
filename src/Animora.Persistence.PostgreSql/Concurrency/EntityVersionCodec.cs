namespace Animora.Persistence.PostgreSql.Concurrency;

/// <summary>
/// Wire codec for the opaque entity version token. Canonical form is the lowercase
/// hyphenated GUID; clients may only compare tokens for equality, never order them.
/// </summary>
public static class EntityVersionCodec
{
    /// <summary>Formats a database version as its canonical wire string.</summary>
    public static string ToWire(Guid version) => version.ToString("D");

    /// <summary>Parses a wire version; <c>false</c> when malformed.</summary>
    public static bool TryParse(string? value, out Guid version) => Guid.TryParse(value, out version);
}
