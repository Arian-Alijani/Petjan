namespace Animora.Server.Files;

/// <summary>
/// The server's own allowlist for uploaded payloads, mirroring <c>Animora.Domain.Patients.DocumentFilePolicy</c>:
/// images and PDF only. The client checks the same list first (so a rejected file is never uploaded), but the
/// server never trusts that — it re-checks the content type, the extension and the magic bytes here.
/// </summary>
internal static class FileTypePolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    { "image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf" };
    // IMP-08: extensions each allowed content type may carry; an empty/absent extension is tolerated (names are display-only).
    private static readonly Dictionary<string, string[]> Extensions = new(StringComparer.Ordinal)
    {
        ["image/jpeg"] = [".jpg", ".jpeg", ".jpe"], ["image/png"] = [".png"], ["image/gif"] = [".gif"],
        ["image/webp"] = [".webp"], ["application/pdf"] = [".pdf"],
    };

    /// <summary>IMP-08: true when the file name's extension (if any) is consistent with the declared content type.</summary>
    public static bool ExtensionMatches(string type, string? fileName)
    {
        string ext = Path.GetExtension(fileName ?? string.Empty);
        if (ext.Length == 0) return true;
        return Extensions.TryGetValue(type, out var allowed) && allowed.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsAllowed(string? type) => type is not null && Allowed.Contains(type);
    public static bool Matches(string type, ReadOnlySpan<byte> prefix)
    {
        if (!IsAllowed(type) || prefix.StartsWith("MZ"u8) || prefix.StartsWith(new byte[] { 0x7f, 0x45, 0x4c, 0x46 })) return false;
        return type switch
        {
            "image/jpeg" => prefix.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
            "image/png" => prefix.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/gif" => prefix.StartsWith("GIF87a"u8) || prefix.StartsWith("GIF89a"u8),
            "image/webp" => prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WEBP"u8),
            "application/pdf" => prefix.StartsWith("%PDF-"u8),
            _ => false
        };
    }
}
