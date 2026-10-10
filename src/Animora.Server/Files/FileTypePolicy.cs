namespace Animora.Server.Files;

internal static class FileTypePolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    { "image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf", "text/plain" };
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
            "text/plain" => !prefix.Contains((byte)0),
            _ => false
        };
    }
}
