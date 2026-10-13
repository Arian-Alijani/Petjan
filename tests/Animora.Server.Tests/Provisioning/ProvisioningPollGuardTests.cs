using Xunit;

namespace Animora.Server.Tests.Provisioning;

/// <summary>
/// §6.1: the GET /intents/{key} poll handler must guard Unprotect + Deserialize so a corrupted ResultJson or a
/// TempPassword protected under a rotated/purged key maps to a typed ProvisioningSignatureInvalid instead of a
/// raw 500 that leaves the intent un-pollable. Asserted against source (no Postgres), matching the repo's style.
/// </summary>
public sealed class ProvisioningPollGuardTests
{
    private static string Source() =>
        Animora.Server.Tests.Licensing.LicensingSource.Read("src/Animora.Server/Provisioning/ProvisioningEndpoints.cs");

    [Fact]
    public void PollHandler_WrapsUnprotectAndDeserialize_InTypedCatch()
    {
        string src = Source();
        // The read-side decode must be inside a try/catch that maps to a typed provisioning failure.
        Assert.Contains("catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)", src, StringComparison.Ordinal);
        Assert.Contains("OperatorEndpoints.Failure(ErrorCodes.ProvisioningSignatureInvalid, http)", src, StringComparison.Ordinal);
    }

    [Fact]
    public void PollHandler_UnprotectAndDeserialize_AreInsideTheTryBlock()
    {
        string src = Source();
        int tryIdx = src.IndexOf("            try", StringComparison.Ordinal);
        int unprotectIdx = src.IndexOf(".Unprotect(Encoding.UTF8.GetString(result.ProtectedTempPassword))", StringComparison.Ordinal);
        int deserializeIdx = src.IndexOf("JsonSerializer.Deserialize<JsonElement>(result.ResultJson)", StringComparison.Ordinal);
        int catchIdx = src.IndexOf("            catch (Exception ex) when", StringComparison.Ordinal);
        Assert.True(tryIdx >= 0, "try block not found");
        Assert.True(catchIdx > tryIdx, "typed catch must follow the try");
        // Both decode calls must sit between the try and the catch.
        Assert.True(deserializeIdx > tryIdx && deserializeIdx < catchIdx, "Deserialize must be inside the try block");
        Assert.True(unprotectIdx > tryIdx && unprotectIdx < catchIdx, "Unprotect must be inside the try block");
    }
}
