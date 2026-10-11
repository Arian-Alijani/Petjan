using System;
using System.Threading.Tasks;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Identity;
using Animora.Server.Licensing.Verification;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>G06: the sealed licence cache over a real migrated SQLite file — every write is merged forward-only.</summary>
public sealed class SealedEntitlementStoreTests
{
    private static readonly DateTimeOffset Issued = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    private sealed class Rig(LocalStoreTestFixture fixture) : IDisposable
    {
        public TestLicenseAuthority Authority { get; } = new(Issued.AddDays(-1));

        public string Tenant { get; } = IdGenerator.NewId();

        public SealedEntitlementStore Store { get; } = new(fixture.Services.GetRequiredService<IServiceScopeFactory>());

        public SealedEntitlement Make(long sequence, long highWaterMs, string? hash = null, string? token = null) =>
            new(token ?? Authority.Issue(TestLicenseAuthority.Payload(Tenant, hash ?? Hash, Issued, sequence)), highWaterMs, sequence, Tenant, hash ?? Hash,
                Issued.ToUnixTimeMilliseconds());

        public void Dispose() => fixture.Dispose();
    }

    private static async Task<Rig> NewRigAsync()
    {
        var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        return new Rig(fixture);
    }

    [Fact]
    public async Task EmptyStore_ReadsNull()
    {
        using var rig = await NewRigAsync();
        var read = await rig.Store.ReadAsync();
        Assert.True(read.IsSuccess);
        Assert.Null(read.Value);
    }

    [Fact]
    public async Task Write_RoundTrips()
    {
        using var rig = await NewRigAsync();
        var first = rig.Make(sequence: 3, highWaterMs: 5_000);

        Assert.True((await rig.Store.WriteAsync(first)).IsSuccess);
        var read = (await rig.Store.ReadAsync()).Value;

        Assert.Equal(first with { }, read);
    }

    [Fact]
    public async Task Write_IsForwardOnly_IgnoresLowerHighWaterOrSequence()
    {
        using var rig = await NewRigAsync();
        var current = rig.Make(sequence: 5, highWaterMs: 9_000);
        Assert.True((await rig.Store.WriteAsync(current)).IsSuccess);

        // A lower sequence is a rollback: rejected, and the stored value is untouched.
        var rollback = await rig.Store.WriteAsync(rig.Make(sequence: 4, highWaterMs: 99_000));
        Assert.True(rollback.IsFailure);
        Assert.Equal(ErrorCodes.LicenseInvalid, rollback.Error);
        Assert.Equal("sequence_rollback", rollback.Detail);
        Assert.Equal(current, (await rig.Store.ReadAsync()).Value);

        // Same sequence + same token with a LOWER high-water: accepted, but the high-water never decreases.
        Assert.True((await rig.Store.WriteAsync(current with { HighWaterUnixMs = 1_000 })).IsSuccess);
        Assert.Equal(9_000, (await rig.Store.ReadAsync()).Value!.HighWaterUnixMs);

        // A higher sequence with a lower high-water advances the licence yet keeps the larger high-water.
        var next = rig.Make(sequence: 6, highWaterMs: 2_000);
        Assert.True((await rig.Store.WriteAsync(next)).IsSuccess);
        var stored = (await rig.Store.ReadAsync()).Value!;
        Assert.Equal(6, stored.HighestSequenceSeen);
        Assert.Equal(next.LicenseToken, stored.LicenseToken);
        Assert.Equal(9_000, stored.HighWaterUnixMs);

        // A higher high-water moves it forward.
        Assert.True((await rig.Store.WriteAsync(next with { HighWaterUnixMs = 12_000 })).IsSuccess);
        Assert.Equal(12_000, (await rig.Store.ReadAsync()).Value!.HighWaterUnixMs);
    }

    [Fact]
    public async Task Write_SameSequenceDifferentToken_AndForeignInstallation_AreRejected()
    {
        using var rig = await NewRigAsync();
        Assert.True((await rig.Store.WriteAsync(rig.Make(sequence: 2, highWaterMs: 1_000))).IsSuccess);

        var conflict = await rig.Store.WriteAsync(rig.Make(sequence: 2, highWaterMs: 1_000)); // fresh token, same sequence
        Assert.Equal(ErrorCodes.LicenseInvalid, conflict.Error);
        Assert.Equal("sequence_token_conflict", conflict.Detail);

        var foreign = await rig.Store.WriteAsync(rig.Make(sequence: 3, highWaterMs: 1_000, hash: new string('b', 64)));
        Assert.Equal(ErrorCodes.LicenseDeviceMismatch, foreign.Error);
    }

    [Fact]
    public async Task Write_RejectsMalformedValues_WithoutTouchingTheStore()
    {
        using var rig = await NewRigAsync();

        Assert.Equal(ErrorCodes.LicenseInvalid, (await rig.Store.WriteAsync(rig.Make(1, 1_000, token: "garbage"))).Error);
        Assert.Equal(ErrorCodes.LicenseInvalid, (await rig.Store.WriteAsync(rig.Make(1, 1_000) with { HighestSequenceSeen = 0 })).Error);
        Assert.Equal(ErrorCodes.LicenseInvalid, (await rig.Store.WriteAsync(rig.Make(1, 1_000) with { InstallationIdHash = "short" })).Error);
        Assert.Null((await rig.Store.ReadAsync()).Value);
    }

    [Fact]
    public async Task Clear_RemovesEverything()
    {
        using var rig = await NewRigAsync();
        Assert.True((await rig.Store.WriteAsync(rig.Make(1, 1_000))).IsSuccess);

        Assert.True((await rig.Store.ClearAsync()).IsSuccess);

        Assert.Null((await rig.Store.ReadAsync()).Value);
    }
}
