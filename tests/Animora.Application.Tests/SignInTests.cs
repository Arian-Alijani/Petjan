using Animora.Application.UseCases.Identity;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>B03 sign-in: success, wrong password, unknown user, lockout, disabled, enumeration safety.</summary>
public sealed class SignInTests
{
    private const string Username = "owner";
    private const string Password = "Secret123";

    private static async Task<Result<SignInResult>> SignInAsync(IdentityWorld world, string username, string password) =>
        await world.SignIn().HandleAsync(new SignInCommand(username, password, false), world.Context(), CancellationToken.None);

    private static IdentityWorld WorldWithOwner(out User owner)
    {
        var world = new IdentityWorld();
        owner = world.AddUser(Username, world.Hasher.Hash(Password));
        return world;
    }

    [Fact]
    public async Task CorrectCredentials_Succeed_AndStartSession()
    {
        var world = WorldWithOwner(out var owner);

        var result = await SignInAsync(world, Username, Password);

        Assert.True(result.IsSuccess);
        Assert.Equal(owner.Id, result.Value!.UserId);
        Assert.Equal(StaffRole.Manager, result.Value.Role);
        Assert.Equal([owner.Id], world.Sessions.StartedFor);
        Assert.Equal(owner.Id, world.Cache.Current?.UserId);
    }

    [Fact]
    public async Task WrongPassword_ReturnsCredentialsInvalid()
    {
        var world = WorldWithOwner(out _);

        var result = await SignInAsync(world, Username, "Wrong1234");

        Assert.Equal(ErrorCodes.CredentialsInvalid, result.Error);
        Assert.Empty(world.Sessions.StartedFor);
    }

    [Fact]
    public async Task UnknownUsername_ReturnsCredentialsInvalid()
    {
        var world = WorldWithOwner(out _);

        var result = await SignInAsync(world, "nobody", Password);

        Assert.Equal(ErrorCodes.CredentialsInvalid, result.Error);
        Assert.Empty(world.Sessions.StartedFor);
    }

    [Fact]
    public async Task UnknownUsername_CostsOneVerification_AndIsIndistinguishableFromWrongPassword()
    {
        var world = WorldWithOwner(out _);

        var before = world.Counting.VerifyCount;
        var unknown = await SignInAsync(world, "nobody", "Wrong1234");
        Assert.Equal(before + 1, world.Counting.VerifyCount); // dummy-hash verification (timing equalizer)

        before = world.Counting.VerifyCount;
        var wrong = await SignInAsync(world, Username, "Wrong1234");
        Assert.Equal(before + 1, world.Counting.VerifyCount);

        Assert.True(unknown.IsFailure);
        Assert.Equal(wrong.Error, unknown.Error);
        Assert.Equal(wrong.Detail, unknown.Detail);
    }

    [Fact]
    public async Task FifthFailure_LocksOut_SixthIsRejectedWithoutHashComparison()
    {
        var world = WorldWithOwner(out _);

        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(ErrorCodes.CredentialsInvalid, (await SignInAsync(world, Username, "Wrong1234")).Error);
        }

        Assert.Equal(ErrorCodes.UserLockedOut, (await SignInAsync(world, Username, "Wrong1234")).Error);

        var verifies = world.Counting.VerifyCount;
        var sixth = await SignInAsync(world, Username, Password); // even the right password
        Assert.Equal(ErrorCodes.UserLockedOut, sixth.Error);
        Assert.Equal(verifies, world.Counting.VerifyCount);
        Assert.Empty(world.Sessions.StartedFor);
    }

    [Fact]
    public async Task Lockout_Expires_After15Minutes()
    {
        var world = WorldWithOwner(out _);
        for (var i = 0; i < User.MaxFailedAttempts; i++) await SignInAsync(world, Username, "Wrong1234");

        world.Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(ErrorCodes.UserLockedOut, (await SignInAsync(world, Username, Password)).Error);

        world.Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await SignInAsync(world, Username, Password);

        Assert.True(result.IsSuccess);
        Assert.Single(world.Sessions.StartedFor);
    }

    [Fact]
    public async Task DisabledUser_ReturnsUserDisabled_WithoutHashComparison()
    {
        var world = WorldWithOwner(out var owner);
        owner.Disable();

        var verifies = world.Counting.VerifyCount;
        var result = await SignInAsync(world, Username, Password);

        Assert.Equal(ErrorCodes.UserDisabled, result.Error);
        Assert.Equal(verifies, world.Counting.VerifyCount);
        Assert.Empty(world.Sessions.StartedFor);
    }
}
