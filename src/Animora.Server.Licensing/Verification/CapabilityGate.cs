using Animora.Domain.Identity;
namespace Animora.Server.Licensing.Verification;

public static class CapabilityGate
{
    public static bool IsAllowed(LicenseState state, RiskLevel risk, bool isRead = false) => state switch
    {
        LicenseState.Valid => Enum.IsDefined(risk),
        LicenseState.Grace => risk is RiskLevel.Low or RiskLevel.Normal or RiskLevel.High,
        LicenseState.ReadOnly => isRead,
        _ => false,
    };
}
