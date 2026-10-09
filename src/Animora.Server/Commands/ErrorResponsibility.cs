using System.Collections.Frozen;

namespace Animora.Server.Commands;

internal static class ErrorResponsibility
{
    private static readonly FrozenDictionary<string, string> Roles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PERMISSION_DENIED"] = "Manager", ["PERMISSION_UNKNOWN"] = "Manager",
        ["ENTITLEMENT_REQUIRED"] = "Manager", ["CONCURRENCY_CONFLICT"] = "Manager",
        ["VALIDATION_FAILED"] = "Receptionist", ["IDEMPOTENCY_FINGERPRINT_MISMATCH"] = "Manager",
        ["TENANT_MISMATCH"] = "Manager", ["RULESET_UNSUPPORTED"] = "Manager",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string For(string code) => Roles.GetValueOrDefault(code, "Manager");
}
