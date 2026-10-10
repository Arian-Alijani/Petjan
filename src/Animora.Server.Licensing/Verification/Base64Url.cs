namespace Animora.Server.Licensing.Verification;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length == 0 || text.Length % 4 == 1 || text.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))) return false;
        try
        {
            bytes = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
            return Encode(bytes) == text; // Reject noncanonical padding bits as well as explicit padding.
        }
        catch (FormatException) { return false; }
    }
}
