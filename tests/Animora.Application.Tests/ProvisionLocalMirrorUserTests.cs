using Animora.Application.Abstractions;
using Animora.Application.UseCases.Identity;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>TASK-1 (RC-A): a server-authenticated, server-created user gets a local mirror so local sign-in succeeds.</summary>
public sealed class ProvisionLocalMirrorUserTests
{
    private const string Password = "Secret456";

    private static ProvisionLocalMirrorUserCommandHandler Handler(IdentityWorld world) =>
        new(world.Users, world.Memberships, new SeededRoleRepository(world.Clock), world.Hasher, world.UnitOfWork, world.Clock);

    private static Task<Result<Unit>> ProvisionAsync(IdentityWorld world, string username, string password, StaffRole role) =>
        Handler(world).HandleAsync(new ProvisionLocalMirrorUserCommand(username, username, password, role),
            world.Context(), CancellationToken.None);

    [Fact]
    public async Task UnknownServerUser_IsMirrored_AndCanThenSignInLocally()
    {
        var world = new IdentityWorld();
        world.AddUser("owner", world.Hasher.Hash("Secret123")); // the local owner already exists (IsFirstRun == false)

        var before = await world.SignIn().HandleAsync(new SignInCommand("naser", Password, false), world.Context(),
            CancellationToken.None);
        Assert.Equal(ErrorCodes.CredentialsInvalid, before.Error);

        Assert.True((await ProvisionAsync(world, "naser", Password, StaffRole.Cashier)).IsSuccess);

        var user = Assert.Single(world.Users.Users, u => u.UserName == "naser");
        Assert.Equal(Tenant.LocalDefaultId, user.TenantId);
        var membership = Assert.Single(world.Memberships.Memberships, m => m.UserId == user.Id);
        Assert.Equal(StaffRole.Cashier, membership.Role);
        Assert.True(membership.IsActive);

        var after = await world.SignIn().HandleAsync(new SignInCommand("naser", Password, false), world.Context(),
            CancellationToken.None);
        Assert.True(after.IsSuccess);
        Assert.Equal(StaffRole.Cashier, after.Value!.Role);
    }

    [Fact]
    public async Task WrongPassword_StillFailsLocalSignIn_AfterMirrorIsWritten()
    {
        var world = new IdentityWorld();
        Assert.True((await ProvisionAsync(world, "naser", Password, StaffRole.Cashier)).IsSuccess);

        var wrong = await world.SignIn().HandleAsync(new SignInCommand("naser", "Wrong-pass1", false), world.Context(),
            CancellationToken.None);

        Assert.Equal(ErrorCodes.CredentialsInvalid, wrong.Error);
    }

    [Fact]
    public async Task Provisioning_IsIdempotent_AndRefreshesAChangedPassword()
    {
        var world = new IdentityWorld();
        Assert.True((await ProvisionAsync(world, "naser", Password, StaffRole.Cashier)).IsSuccess);
        Assert.True((await ProvisionAsync(world, "naser", Password, StaffRole.Cashier)).IsSuccess);

        Assert.Single(world.Users.Users);
        Assert.Single(world.Memberships.Memberships);

        Assert.True((await ProvisionAsync(world, "naser", "Changed789", StaffRole.Cashier)).IsSuccess);
        var signIn = await world.SignIn().HandleAsync(new SignInCommand("naser", "Changed789", false), world.Context(),
            CancellationToken.None);
        Assert.True(signIn.IsSuccess);
    }

    [Fact]
    public async Task MissingRoleDefinition_FailsClosed_WithoutWritingAnything()
    {
        var world = new IdentityWorld();
        var handler = new ProvisionLocalMirrorUserCommandHandler(world.Users, world.Memberships, new NullRoleRepository(),
            world.Hasher, world.UnitOfWork, world.Clock);

        var result = await handler.HandleAsync(new ProvisionLocalMirrorUserCommand("naser", "naser", Password, StaffRole.Cashier),
            world.Context(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(world.Users.Users);
        Assert.Empty(world.Memberships.Memberships);
    }
    [Fact]
    public async Task ServerUserId_BecomesTheLocalUserId_AndItsMembership()
    {
        var world = new IdentityWorld();
        var result = await Handler(world).HandleAsync(
            new ProvisionLocalMirrorUserCommand("dr.sara", "dr.sara", Password, StaffRole.Doctor, "01M4X2FVSERVERUSER0000000"),
            world.Context(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(world.Users.Users);
        Assert.Equal("01M4X2FVSERVERUSER0000000", user.Id);
        var membership = Assert.Single(world.Memberships.Memberships);
        Assert.Equal(user.Id, membership.UserId);
        Assert.Equal(StaffRole.Doctor, membership.Role);
    }

    [Fact]
    public async Task LegacyRandomIdMirror_IsSuperseded_ByTheServerIdentifiedUser()
    {
        var world = new IdentityWorld();
        Assert.True((await ProvisionAsync(world, "dr.sara", Password, StaffRole.Doctor)).IsSuccess);
        var legacy = Assert.Single(world.Users.Users);

        var result = await Handler(world).HandleAsync(
            new ProvisionLocalMirrorUserCommand("dr.sara", "dr.sara", Password, StaffRole.Doctor, "SERVER-ID-1"),
            world.Context(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Disabled, legacy.Status);
        Assert.NotEqual("dr.sara", legacy.UserName);
        Assert.DoesNotContain(world.Memberships.Memberships, m => m.UserId == legacy.Id && m.IsActive);
        var active = Assert.Single(world.Memberships.Memberships, m => m.IsActive);
        Assert.Equal("SERVER-ID-1", active.UserId);

        var signIn = await world.SignIn().HandleAsync(new SignInCommand("dr.sara", Password, false), world.Context(),
            CancellationToken.None);
        Assert.True(signIn.IsSuccess);
        Assert.Equal("SERVER-ID-1", signIn.Value!.UserId);
    }

    [Fact]
    public async Task ServerRoleChange_IsReconciled_OnTheServerIdentifiedUser()
    {
        var world = new IdentityWorld();
        var first = new ProvisionLocalMirrorUserCommand("naser", "naser", Password, StaffRole.Cashier, "SERVER-ID-2");
        Assert.True((await Handler(world).HandleAsync(first, world.Context(), CancellationToken.None)).IsSuccess);
        Assert.True((await Handler(world).HandleAsync(first with { Role = StaffRole.Doctor }, world.Context(),
            CancellationToken.None)).IsSuccess);

        Assert.Single(world.Users.Users);
        Assert.Equal(StaffRole.Doctor, Assert.Single(world.Memberships.Memberships).Role);
    }
}
