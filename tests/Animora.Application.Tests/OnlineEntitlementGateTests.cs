using System.Net;
using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Client.Services.Identity;
using Animora.Client.Services.Security;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Identity;
using Animora.Persistence.Tests;
using Animora.Server.Licensing.Verification;
using Animora.Sync.Custom;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// G06 entitlement plane: <see cref="OnlineEntitlementGate"/> decides from the sealed local licence only (verified signature +
/// <see cref="LicensePolicy"/> at <c>max(now, high-water)</c> + <see cref="CapabilityGate"/>) — never from a server call, and a backward OS
/// clock can never revive an expired licence.
/// </summary>
public sealed class OnlineEntitlementGateTests
{
    private static readonly DateTimeOffset Issued = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    [RequiresPermission(PermissionCodes.SalesVoid)]
    private sealed record CriticalCommand;

    [RequiresPermission(PermissionCodes.SalesPost)]
    private sealed record HighCommand;

    [RequiresPermission(PermissionCodes.PatientsWrite)]
    private sealed record NormalCommand;

    private sealed record UndeclaredCommand;

    private sealed record SomeQuery;

    private static readonly CommandContext CommandCtx = new("T", "U", "D", "OP", "C", null, null, null, Issued);
    private static readonly QueryContext QueryCtx = new("T", "U", "C");

    private sealed class MemorySealedStore : ISealedEntitlementStore
    {
        public SealedEntitlement? Value { get; set; }

        public Task<Result<SealedEntitlement?>> ReadAsync(CancellationToken ct = default) => Task.FromResult(Result<SealedEntitlement?>.Ok(Value));

        public Task<Result> WriteAsync(SealedEntitlement entitlement, CancellationToken ct = default)
        {
            Value = entitlement;
            return Task.FromResult(Result.Ok());
        }
    }

    private sealed class Harness
    {
        public Harness(DateTimeOffset now, DateTimeOffset? sealedHighWater = null, DateTimeOffset? monotonicStart = null)
        {
            Authority = new TestLicenseAuthority(Issued.AddDays(-1));
            Fingerprint = new FixedFingerprintSource("machine:TEST", "os:UNIX");
            Installation = InstallationHash.Compute(Fingerprint);
            TenantId = IdGenerator.NewId();
            Clock = new MutableClock(now);
            Monotonic = new FakeMonotonicClock(monotonicStart ?? now);
            Keys = new LicenseKeyCache(new MemorySecretStore());
            Assert.True(Keys.Store(Authority.ToWire(Issued)).IsSuccess);
            SealedStore = new MemorySealedStore();
            Seal(Authority.Issue(TestLicenseAuthority.Payload(TenantId, Installation, Issued)), sealedHighWater ?? Issued);
            Gate = new OnlineEntitlementGate(SealedStore, Keys, Fingerprint, Clock, Monotonic);
        }

        public TestLicenseAuthority Authority { get; }

        public FixedFingerprintSource Fingerprint { get; }

        public string Installation { get; }

        public string TenantId { get; }

        public MutableClock Clock { get; }

        public FakeMonotonicClock Monotonic { get; }

        public LicenseKeyCache Keys { get; }

        public MemorySealedStore SealedStore { get; }

        public OnlineEntitlementGate Gate { get; }

        public void Seal(string token, DateTimeOffset highWater) => SealedStore.Value = new SealedEntitlement(token, highWater.ToUnixTimeMilliseconds(), 1,
            TenantId, Installation, Issued.ToUnixTimeMilliseconds());

        public async Task<bool> CommandAllowedAsync(object command) => (await Gate.EvaluateAsync(command, CommandCtx, default)).IsSuccess;

        public async Task<bool> QueryAllowedAsync() => (await Gate.EvaluateAsync(new SomeQuery(), QueryCtx, default)).IsSuccess;
    }

    [Fact]
    public async Task ValidLicense_AllowsByRisk()
    {
        var h = new Harness(Issued.AddDays(1));

        Assert.Equal(LicenseState.Valid, (await h.Gate.GetVerdictAsync()).State);
        Assert.True(await h.CommandAllowedAsync(new UndeclaredCommand()));
        Assert.True(await h.CommandAllowedAsync(new NormalCommand()));
        Assert.True(await h.CommandAllowedAsync(new HighCommand()));
        Assert.True(await h.CommandAllowedAsync(new CriticalCommand()));
        Assert.True(await h.QueryAllowedAsync());
    }

    [Fact]
    public async Task Grace_AllowsLowNormalHigh_DeniesCritical()
    {
        var h = new Harness(Issued.AddDays(45)); // expired at +40d, grace until +54d

        Assert.Equal(LicenseState.Grace, (await h.Gate.GetVerdictAsync()).State);
        Assert.True(await h.QueryAllowedAsync());
        Assert.True(await h.CommandAllowedAsync(new NormalCommand()));
        Assert.True(await h.CommandAllowedAsync(new HighCommand()));
        var critical = await h.Gate.EvaluateAsync(new CriticalCommand(), CommandCtx, default);
        Assert.True(critical.IsFailure);
        Assert.Equal(ErrorCodes.EntitlementRequired, critical.Error);
    }

    [Fact]
    public async Task ReadOnly_AllowsQuery_DeniesCommand()
    {
        var h = new Harness(Issued.AddDays(60)); // past grace, inside the 60-day read-only window

        Assert.Equal(LicenseState.ReadOnly, (await h.Gate.GetVerdictAsync()).State);
        Assert.True(await h.QueryAllowedAsync());
        Assert.False(await h.CommandAllowedAsync(new NormalCommand()));
        Assert.False(await h.CommandAllowedAsync(new UndeclaredCommand()));
    }

    [Fact]
    public async Task Locked_DeniesEverything()
    {
        var h = new Harness(Issued.AddDays(300)); // far beyond grace + read-only window

        Assert.Equal(LicenseState.Locked, (await h.Gate.GetVerdictAsync()).State);
        Assert.False(await h.QueryAllowedAsync());
        Assert.False(await h.CommandAllowedAsync(new NormalCommand()));
    }

    [Fact]
    public async Task NoSealedLicense_UnknownKey_OrOtherInstallation_FailClosed()
    {
        var none = new Harness(Issued.AddDays(1));
        none.SealedStore.Value = null;
        Assert.False(await none.QueryAllowedAsync());
        Assert.Equal("NO_LICENSE", (await none.Gate.GetVerdictAsync()).Reason);

        // Signed by a key the client does not trust.
        var unknownKey = new Harness(Issued.AddDays(1));
        var stranger = new TestLicenseAuthority(Issued.AddDays(-1));
        unknownKey.Seal(stranger.Issue(TestLicenseAuthority.Payload(unknownKey.TenantId, unknownKey.Installation, Issued)), Issued);
        Assert.False(await unknownKey.QueryAllowedAsync());

        // Genuinely signed, but bound to another machine.
        var foreign = new Harness(Issued.AddDays(1));
        foreign.Seal(foreign.Authority.Issue(TestLicenseAuthority.Payload(foreign.TenantId, new string('b', 64), Issued)), Issued);
        var verdict = await foreign.Gate.GetVerdictAsync();
        Assert.Equal(LicenseState.Locked, verdict.State);
        Assert.Equal("WRONG_INSTALLATION", verdict.Reason);

        // Garbage token.
        var garbage = new Harness(Issued.AddDays(1));
        garbage.Seal("not-a-token", Issued);
        Assert.False(await garbage.CommandAllowedAsync(new NormalCommand()));
    }

    [Fact]
    public async Task BackwardClock_DoesNotReviveExpired()
    {
        // The licence expired long ago and the sealed high-water proves the device already saw that time.
        // Now the OS clock is wound back to the day it was issued.
        var h = new Harness(now: Issued, sealedHighWater: Issued.AddDays(60), monotonicStart: Issued);

        var verdict = await h.Gate.GetVerdictAsync();

        Assert.Equal(LicenseState.ReadOnly, verdict.State);
        Assert.True(verdict.ClockRollbackDetected);
        Assert.False(await h.CommandAllowedAsync(new NormalCommand()));
        Assert.True(await h.QueryAllowedAsync());
    }

    [Fact]
    public async Task BackwardClock_AgainstMonotonicHighWater_AlsoHolds()
    {
        // No sealed high-water help: only the persisted monotonic clock remembers the later time.
        var h = new Harness(now: Issued, sealedHighWater: Issued, monotonicStart: Issued.AddDays(300));

        Assert.Equal(LicenseState.Locked, (await h.Gate.GetVerdictAsync()).State);
        Assert.False(await h.QueryAllowedAsync());
    }

    [Fact]
    public async Task DecidesWithoutServerCall()
    {
        var hits = 0;
        using var fixture = new LocalStoreTestFixture(configureServices: services =>
        {
            services.AddSingleton<Animora.Infrastructure.Local.Security.ISecretStore>(new MemorySecretStore());
            services.AddSingleton<IDeviceFingerprintSource>(new FixedFingerprintSource("machine:TEST", "os:UNIX"));
            services.AddAnimoraHttpSyncEngine(o => o.BaseAddress = "https://animora.example/");
            services.AddHttpClient(SyncFeedClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new CountingHandler(() => Interlocked.Increment(ref hits)));
            services.AddAnimoraOnlineIdentity();
        });
        await fixture.MigrateAsync();

        var authority = new TestLicenseAuthority(Issued.AddDays(-1));
        var installation = InstallationHash.Compute(fixture.Services.GetRequiredService<IDeviceFingerprintSource>());
        var tenantId = IdGenerator.NewId();
        var token = authority.Issue(TestLicenseAuthority.Payload(tenantId, installation, Issued));
        Assert.True(fixture.Services.GetRequiredService<LicenseKeyCache>().Store(authority.ToWire(Issued)).IsSuccess);
        var sealedStore = fixture.Services.GetRequiredService<ISealedEntitlementStore>();
        Assert.True((await sealedStore.WriteAsync(new SealedEntitlement(token, Issued.ToUnixTimeMilliseconds(), 1,
            tenantId, installation, Issued.ToUnixTimeMilliseconds()))).IsSuccess);
        fixture.Clock.Now = Issued.AddDays(1);

        var gate = fixture.Services.GetRequiredService<IEntitlementGate>();
        Assert.IsType<OnlineEntitlementGate>(gate);
        for (var i = 0; i < 5; i++)
        {
            Assert.True((await gate.EvaluateAsync(new NormalCommand(), CommandCtx, default)).IsSuccess);
            Assert.True((await gate.EvaluateAsync(new SomeQuery(), QueryCtx, default)).IsSuccess);
        }

        Assert.Equal(0, hits); // the entitlement plane never touched the network
    }

    private sealed class CountingHandler(Action onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
