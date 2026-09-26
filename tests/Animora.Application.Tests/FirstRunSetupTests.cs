using Animora.Application.UseCases.Identity;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>B03 first-run: owner + Manager membership + approved device exactly once; no new tenant.</summary>
public sealed class FirstRunSetupTests
{
    private static readonly CompleteFirstRunSetupCommand Command =
        new("Clinic A", "Owner Person", "Owner", "Secret123");

    [Fact]
    public async Task FirstRun_CreatesOwnerMembershipDevice_RenamesDefaultTenant_AndStartsSession()
    {
        var world = new IdentityWorld();

        var result = await world.FirstRun().HandleAsync(Command, world.Context(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Tenant.LocalDefaultId, world.Users.Tenant.Id);
        Assert.Equal("Clinic A", world.Users.Tenant.DisplayName);

        var owner = Assert.Single(world.Users.Users);
        Assert.Equal(Tenant.LocalDefaultId, owner.TenantId);
        Assert.Equal("owner", owner.UserName);
        Assert.Equal(1, world.Counting.HashCount);
        Assert.NotNull(owner.PasswordHash);
        Assert.NotEqual("Secret123", owner.PasswordHash);

        var membership = Assert.Single(world.Memberships.Memberships);
        Assert.Equal(owner.Id, membership.UserId);
        Assert.Equal(StaffRole.Manager, membership.Role);
        Assert.True(membership.IsActive);

        var device = Assert.Single(world.Devices.Devices);
        Assert.Equal(owner.Id, device.UserId);
        Assert.True(device.IsUsable);

        Assert.Equal([owner.Id], world.Sessions.StartedFor);
        Assert.Equal(owner.Id, result.Value!.UserId);
    }

    [Fact]
    public async Task SecondInvocation_ReturnsFirstRunAlreadyCompleted_WithoutDuplicates()
    {
        var world = new IdentityWorld();
        Assert.True((await world.FirstRun().HandleAsync(Command, world.Context(), CancellationToken.None)).IsSuccess);

        var second = await world.FirstRun().HandleAsync(
            new CompleteFirstRunSetupCommand("Clinic B", "Other", "other", "Secret456"), world.Context(), CancellationToken.None);

        Assert.Equal(ErrorCodes.FirstRunAlreadyCompleted, second.Error);
        Assert.Single(world.Users.Users);
        Assert.Single(world.Memberships.Memberships);
        Assert.Single(world.Devices.Devices);
        Assert.Single(world.Sessions.StartedFor);
        Assert.Equal("Clinic A", world.Users.Tenant.DisplayName);
    }
}
