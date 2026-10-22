using Animora.Application.UseCases.Identity;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// B03 session lifecycle over the real <c>LocalSessionService</c> and a temporary file-backed SQLite store
/// (never <c>:memory:</c>); time only moves through the injected mutable clock.
/// </summary>
public sealed class SessionLifecycleTests
{
    private const string Password = "Secret123";

    private static async Task<LocalIdentityHost> StartedHostAsync()
    {
        var host = new LocalIdentityHost();
        await host.InitializeAsync();
        var setup = await host.SendAnonymousAsync(new CompleteFirstRunSetupCommand("Clinic", "Owner", "owner", Password));
        Assert.True(setup.IsSuccess, setup.Detail);
        return host;
    }

    [Fact]
    public async Task StartedSession_IsActiveAndUnlocked()
    {
        await using var host = await StartedHostAsync();

        var restored = await host.RestoreAsync();

        Assert.True(restored.IsSuccess);
        Assert.NotNull(restored.Value!.UserId);
        Assert.False(restored.Value.IsLocked);
        Assert.Equal(MutableClock.Start.AddHours(12), restored.Value.ExpiresAtUtc);
        Assert.True((await host.BuildUserContextAsync()).IsSuccess);
    }

    [Fact]
    public async Task LongInactivity_NeverLocks_WhileWithinAbsoluteLifetime()
    {
        // Inactivity must NEVER lock the session (A-RULE 029, revised): leaving the app open and idle, or not
        // interacting for a long stretch, must not drop the shell to the read-only/locked state. Only an explicit
        // lock or the hard absolute lifetime may end usable access.
        await using var host = await StartedHostAsync();

        // Far past the old 30-minute idle window, but still inside the 12-hour absolute lifetime.
        host.Clock.Advance(TimeSpan.FromHours(8));
        var restored = await host.RestoreAsync();

        Assert.True(restored.IsSuccess);
        Assert.False(restored.Value!.IsLocked);
        Assert.False(host.Cache.Current!.IsLocked);
        Assert.NotNull(restored.Value.UserId);
    }

    [Fact]
    public async Task TwelveHours_SignsOutWithSessionExpired_AndEndsTheRow()
    {
        await using var host = await StartedHostAsync();

        host.Clock.Advance(TimeSpan.FromHours(12));
        var restored = await host.RestoreAsync();

        Assert.Equal(ErrorCodes.SessionExpired, restored.Error);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var session = await db.Set<LocalSession>().SingleAsync();
        Assert.NotNull(session.EndedAtUtc);
        Assert.Equal(ErrorCodes.SessionExpired.Code, session.EndReason);
    }

    [Fact]
    public async Task RestoreAfterCrash_KeepsOriginalExpiry_AndIsNeverSilentlyExtended()
    {
        await using var host = await StartedHostAsync();
        var originalExpiry = MutableClock.Start.AddHours(12);

        host.Clock.Advance(TimeSpan.FromMinutes(10));
        await host.RestartAsync(); // new provider + session service over the same database file

        var restored = await host.RestoreAsync();
        Assert.True(restored.IsSuccess);
        Assert.Equal(originalExpiry, restored.Value!.ExpiresAtUtc);

        host.Clock.Advance(TimeSpan.FromHours(12) - TimeSpan.FromMinutes(10));
        await host.RestartAsync();
        Assert.Equal(ErrorCodes.SessionExpired, (await host.RestoreAsync()).Error);
    }

    [Fact]
    public async Task Lock_ThenUnlock_WithCorrectAndWrongPassword()
    {
        await using var host = await StartedHostAsync();

        Assert.True((await host.SendAsUserAsync(new LockSessionCommand())).IsSuccess);
        Assert.True(host.Cache.Current!.IsLocked);

        var wrong = await host.SendAnonymousAsync(new UnlockSessionCommand("Wrong1234"));
        Assert.Equal(ErrorCodes.CredentialsInvalid, wrong.Error);
        Assert.True(host.Cache.Current!.IsLocked);

        var right = await host.SendAnonymousAsync(new UnlockSessionCommand(Password));
        Assert.True(right.IsSuccess);
        Assert.False(right.Value!.IsLocked);
        Assert.False(host.Cache.Current!.IsLocked);
    }

    [Fact]
    public async Task CommandWithoutActiveSession_FailsWithSessionExpired()
    {
        await using var host = new LocalIdentityHost();
        await host.InitializeAsync();

        Assert.Equal(ErrorCodes.SessionExpired, (await host.BuildUserContextAsync()).Error);
        Assert.Equal(ErrorCodes.SessionExpired, (await host.SendAsUserAsync(new SignOutCommand("TEST"))).Error);
    }
}
