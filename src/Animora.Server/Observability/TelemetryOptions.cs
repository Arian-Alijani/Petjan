using System.Net;
using Microsoft.Extensions.Options;
namespace Animora.Server.Observability;

internal sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";
    public bool Enabled { get; set; }
    public string OtlpEndpoint { get; set; } = "http://127.0.0.1:4317";
    public bool AllowLanCollector { get; set; }
}
internal sealed class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>
{
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
    {
        if (!Uri.TryCreate(options.OtlpEndpoint,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return Invalid();
        if (string.Equals(uri.Host,"localhost",StringComparison.OrdinalIgnoreCase)) return ValidateOptionsResult.Success;
        if (!IPAddress.TryParse(uri.Host.Trim('[',']'),out var address)) return Invalid(); // no DNS-rebinding bypass
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return ValidateOptionsResult.Success;
        if (!options.AllowLanCollector) return Invalid();
        byte[] bytes = address.GetAddressBytes();
        bool privateHost = bytes.Length == 4
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
            : bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc;
        return privateHost ? ValidateOptionsResult.Success : Invalid();
    }
    private static ValidateOptionsResult Invalid() => ValidateOptionsResult.Fail("Telemetry collector must be loopback, or an explicitly opted-in private literal address.");
}
