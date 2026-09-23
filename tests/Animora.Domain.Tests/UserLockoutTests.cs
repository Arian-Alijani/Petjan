using System;
using Animora.Domain.Identity;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The failed-attempt lockout state machine: the threshold locks, the window resets a stale
/// streak, and a success clears everything. No test reads the wall clock (CONVENTIONS.md §9).
/// </summary>
public sealed class UserLockoutTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FifthFailedAttempt_LocksWithEndInstant()
    {
        var clock = new TestClock(Start);
        var user = User.Create("T1", "Reza", "رضا", clock);

        for (var i = 1; i < User.MaxFailedAttempts; i++)
        {
            Assert.False(user.RegisterFailedAttempt(clock));
            Assert.Equal(UserStatus.Active, user.Status);
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.True(user.RegisterFailedAttempt(clock));
        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.Equal(User.MaxFailedAttempts, user.FailedAttemptCount);
        Assert.Equal(clock.UtcNow + User.LockoutDuration, user.LockoutEndUtc);
        Assert.True(user.IsLockedOut(clock.UtcNow));
        Assert.False(user.IsLockedOut(clock.UtcNow + User.LockoutDuration + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AttemptsOutsideWindow_DoNotAccumulate()
    {
        var clock = new TestClock(Start);
        var user = User.Create("T1", "Reza", "رضا", clock);
        var window = TimeSpan.FromMinutes(10);

        for (var i = 0; i < User.MaxFailedAttempts - 1; i++)
        {
            user.RegisterFailedAttempt(clock, User.MaxFailedAttempts, window);
        }

        clock.Advance(window + TimeSpan.FromMinutes(1));

        Assert.False(user.RegisterFailedAttempt(clock, User.MaxFailedAttempts, window));
        Assert.Equal(1, user.FailedAttemptCount);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void SuccessfulLogin_ClearsLockout()
    {
        var clock = new TestClock(Start);
        var user = User.Create("T1", "Reza", "رضا", clock);

        for (var i = 0; i < User.MaxFailedAttempts; i++)
        {
            user.RegisterFailedAttempt(clock);
        }

        Assert.Equal(UserStatus.Locked, user.Status);

        user.RegisterSuccessfulLogin(clock);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(0, user.FailedAttemptCount);
        Assert.Null(user.LockoutEndUtc);
        Assert.Equal(clock.UtcNow, user.LastLoginUtc);
        Assert.False(user.IsLockedOut(clock.UtcNow));
    }

    [Fact]
    public void ClearFailures_DoesNotReactivateDisabledAccount()
    {
        var clock = new TestClock(Start);
        var user = User.Create("T1", "Reza", "رضا", clock);
        user.Disable();

        user.ClearFailures();

        Assert.Equal(UserStatus.Disabled, user.Status);
    }

    [Fact]
    public void SetPasswordHash_RotatesSecurityStamp()
    {
        var clock = new TestClock(Start);
        var user = User.Create("T1", "  Reza  ", "رضا", clock);
        var before = user.SecurityStamp;

        user.SetPasswordHash("hash");

        Assert.Equal("reza", user.UserName);
        Assert.Equal("hash", user.PasswordHash);
        Assert.NotEqual(before, user.SecurityStamp);
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
