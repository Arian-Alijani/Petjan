using Animora.Application.Security;
using Animora.Application.UseCases.Identity;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Security;
using Microsoft.AspNetCore.Identity;
using Xunit;
using IdentityOptions = Microsoft.Extensions.Options.Options;

namespace Animora.Application.Tests;

/// <summary>B03 password policy + rehash-on-verify upgrade of a legacy-format hash.</summary>
public sealed class PasswordPolicyTests
{
    private const string Password = "Legacy123";

    [Theory]
    [InlineData("Abc1234")]   // 7 characters
    [InlineData("12345678")]  // no letter
    [InlineData("abcdefgh")]  // no digit
    public void Violations_ReturnPasswordPolicyViolation(string password) =>
        Assert.Equal(ErrorCodes.PasswordPolicyViolation, PasswordPolicy.Validate(password).Error);

    [Fact]
    public void MinimumValidPassword_Succeeds() =>
        Assert.True(PasswordPolicy.Validate("abcdefg1").IsSuccess);

    [Fact]
    public async Task FirstRun_RejectsPolicyViolation()
    {
        var world = new IdentityWorld();

        var result = await world.FirstRun().HandleAsync(
            new CompleteFirstRunSetupCommand("Clinic", "Owner", "owner", "short1"), world.Context(), CancellationToken.None);

        Assert.Equal(ErrorCodes.PasswordPolicyViolation, result.Error);
        Assert.Empty(world.Users.Users);
    }

    [Fact]
    public void LegacyHash_VerifiesWithRehashNeeded()
    {
        var hasher = new PasswordHashingService();

        Assert.Equal(PasswordVerificationOutcome.SuccessRehashNeeded, hasher.Verify(LegacyHash(), Password));
    }

    [Fact]
    public async Task SignIn_WithLegacyHash_ReplacesItWithCurrentFormat()
    {
        var hasher = new PasswordHashingService();
        var world = new IdentityWorld(hasher);
        var legacy = LegacyHash();
        var user = world.AddUser("owner", legacy);

        var result = await world.SignIn().HandleAsync(new SignInCommand("owner", Password, false), world.Context(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(legacy, user.PasswordHash);
        Assert.Equal(PasswordVerificationOutcome.Success, hasher.Verify(user.PasswordHash!, Password));
    }

    /// <summary>ASP.NET Core Identity's own legacy (IdentityV2) format — no custom cryptography.</summary>
    private static string LegacyHash() =>
        new PasswordHasher<object>(IdentityOptions.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
        })).HashPassword(new object(), Password);
}
