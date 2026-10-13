using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>
/// §3.1: disabling a staff member must rotate the user's security stamp so every live access token is
/// invalid on its next request — immediate and consistent with <c>DeviceAdminService.RevokeAsync</c>.
/// Asserted against source so it needs no Postgres (same approach as RoleCatalogueBatchTests).
/// </summary>
public sealed class StaffDisableStampRotationTests
{
    private static string StaffSource() =>
        Animora.Server.Tests.Licensing.LicensingSource.Read("src/Animora.Server/Auth/StaffProvisioningService.cs");

    [Fact]
    public void DisablePath_RotatesSecurityStamp()
    {
        string src = StaffSource();
        // The disable branch (user.Disable()) must be immediately followed by a stamp rotation.
        int disableIdx = src.IndexOf("user.Disable();", StringComparison.Ordinal);
        Assert.True(disableIdx >= 0, "user.Disable() disable branch not found");
        string afterDisable = src[disableIdx..];
        int rotateIdx = afterDisable.IndexOf("user.RotateSecurityStamp();", StringComparison.Ordinal);
        // Allow for the explanatory comment lines between Disable() and the rotation; the window only needs to prove
        // the rotation lives in the same (disable) branch, well before the next method member.
        Assert.True(rotateIdx >= 0 && rotateIdx < 600,
            "user.RotateSecurityStamp() must follow user.Disable() in the disable branch");
    }

    [Fact]
    public void EnablePath_DoesNotRotateSecurityStamp()
    {
        string src = StaffSource();
        // Guard that the enable branch (user.Enable()) is NOT followed by a rotation before the branch closes.
        int enableIdx = src.IndexOf("user.Enable();", StringComparison.Ordinal);
        Assert.True(enableIdx >= 0, "user.Enable() enable branch not found");
        // Window up to the next closing brace of the if-branch (small, local slice).
        string afterEnable = src.Substring(enableIdx, Math.Min(120, src.Length - enableIdx));
        Assert.DoesNotContain("RotateSecurityStamp", afterEnable, StringComparison.Ordinal);
    }
}
