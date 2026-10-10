namespace Animora.Server.Hosting;

internal static class SensitiveLogNames
{
    public static readonly string[] Fragments = ["password", "token", "authorization", "valet", "X-Animora-Valet", "connectionString", "secret", "licenseKey", "payloadJson", "detailJson", "body"];
    public static bool IsSensitive(string name) => Fragments.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
}
