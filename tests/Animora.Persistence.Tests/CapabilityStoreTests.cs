using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Security;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// B04c acceptance for the real B04b <see cref="CapabilityStore"/> over a migrated SQLite <b>file</b> database
/// (never in-memory): replace/get/revoke, per-device and per-tenant isolation, exactly one current row per
/// <c>(tenant,user,device)</c>, and a still-valid signature after revocation.
/// </summary>
public sealed class CapabilityStoreTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A deterministic in-memory <see cref="ISecretStore"/> stands in only for the OS keystore (which does not exist
    /// in this sandbox); the REAL <c>CapabilitySnapshotSigner</c> and <c>CapabilityStore</c> run over it. Registered
    /// before <c>AddAnimoraLocalInfrastructure</c>, whose <c>TryAddSingleton</c> therefore yields to it.
    /// </summary>
    private static LocalStoreTestFixture Fixture() =>
        new(configureServices: services => services.AddSingleton<ISecretStore>(new FixedSecretStore()));

    private sealed class FixedSecretStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _secrets = new(StringComparer.Ordinal);

        public Result<byte[]> TryRead(string name) => _secrets.TryGetValue(name, out var value)
            ? Result<byte[]>.Ok(value)
            : Result<byte[]>.Fail(ErrorCodes.NotFound);

        public Result Write(string name, byte[] value)
        {
            _secrets[name] = value;
            return Result.Ok();
        }

        public Result Delete(string name)
        {
            _secrets.Remove(name);
            return Result.Ok();
        }
    }

    private static StoredCapabilitySnapshot Sign(ICapabilitySnapshotSigner signer, string tenant, string user,
        string device, DateTimeOffset issued, params string[] codes)
    {
        var expiry = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var code in codes) expiry[code] = issued + OfflineCapabilityPolicy.WindowForCode(code);
        var absolute = expiry.Count == 0 ? issued : expiry.Values.Max();
        var unsigned = new StoredCapabilitySnapshot(
            "SNAP-" + Guid.NewGuid().ToString("N"), tenant, user, device,
            CapabilitySnapshotCodec.WritePermissions(codes),
            issued,
            CapabilitySnapshotCodec.WriteExpiry(expiry),
            absolute,
            SnapshotSignature: string.Empty,
            SecurityStamp: "STAMP-1",
            RevokedAtUtc: null);
        return unsigned with { SnapshotSignature = signer.Sign(unsigned).Value! };
    }

    private static async Task<Result<StoredCapabilitySnapshot?>> GetAsync(LocalStoreTestFixture fixture,
        string tenant, string user, string device)
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICapabilityStore>().GetAsync(tenant, user, device);
    }

    private static async Task ReplaceAsync(LocalStoreTestFixture fixture, StoredCapabilitySnapshot snapshot)
    {
        using var scope = fixture.CreateScope();
        var replaced = await scope.ServiceProvider.GetRequiredService<ICapabilityStore>().ReplaceAsync(snapshot);
        Assert.True(replaced.IsSuccess, replaced.Detail);
    }

    private static int CountRows(LocalStoreTestFixture fixture, string tenant, string user, string device)
    {
        using var context = fixture.CreateContext(fixture.TenantId);
        return context.Set<DeviceCapabilitySnapshot>()
            .Count(e => e.TenantId == tenant && e.UserId == user && e.DeviceId == device);
    }

    [Fact]
    public async Task ReplaceThenGet_RoundTrips_AndSignatureStillVerifies()
    {
        using var fixture = Fixture();
        await fixture.MigrateAsync();
        var signer = fixture.Services.GetRequiredService<ICapabilitySnapshotSigner>();
        fixture.Clock.Now = Base;
        var snapshot = Sign(signer, fixture.TenantId, "U-1", "D-1", Base, PermissionCodes.PatientsRead);

        await ReplaceAsync(fixture, snapshot);
        var loaded = await GetAsync(fixture, fixture.TenantId, "U-1", "D-1");

        Assert.True(loaded.IsSuccess);
        Assert.NotNull(loaded.Value);
        Assert.Equal(snapshot.Id, loaded.Value!.Id);
        Assert.Equal(snapshot.PermissionsJson, loaded.Value.PermissionsJson);
        Assert.True(signer.Verify(loaded.Value).IsSuccess);
    }

    [Fact]
    public async Task Get_WithNoSnapshot_ReturnsNullValue()
    {
        using var fixture = Fixture();
        await fixture.MigrateAsync();

        var loaded = await GetAsync(fixture, fixture.TenantId, "U-missing", "D-1");

        Assert.True(loaded.IsSuccess);
        Assert.Null(loaded.Value);
    }

    [Fact]
    public async Task Replace_ReplacesTheCurrentRow_LeavingOnePerTriple()
    {
        using var fixture = Fixture();
        await fixture.MigrateAsync();
        var signer = fixture.Services.GetRequiredService<ICapabilitySnapshotSigner>();

        var first = Sign(signer, fixture.TenantId, "U-1", "D-1", Base, PermissionCodes.PatientsRead);
        var second = Sign(signer, fixture.TenantId, "U-1", "D-1", Base.AddMinutes(1),
            PermissionCodes.PatientsRead, PermissionCodes.PatientsWrite);
        await ReplaceAsync(fixture, first);
        await ReplaceAsync(fixture, second);

        var loaded = await GetAsync(fixture, fixture.TenantId, "U-1", "D-1");
        Assert.Equal(second.Id, loaded.Value!.Id);
        Assert.Equal(1, CountRows(fixture, fixture.TenantId, "U-1", "D-1")); // the unique index guards one row.
    }

    [Fact]
    public async Task DifferentDevices_AreIsolated()
    {
        using var fixture = Fixture();
        await fixture.MigrateAsync();
        var signer = fixture.Services.GetRequiredService<ICapabilitySnapshotSigner>();

        var phone = Sign(signer, fixture.TenantId, "U-1", "D-PHONE", Base, PermissionCodes.PatientsRead);
        var desk = Sign(signer, fixture.TenantId, "U-1", "D-DESK", Base, PermissionCodes.SalesRead);
        await ReplaceAsync(fixture, phone);
        await ReplaceAsync(fixture, desk);

        Assert.Equal(phone.Id, (await GetAsync(fixture, fixture.TenantId, "U-1", "D-PHONE")).Value!.Id);
        Assert.Equal(desk.Id, (await GetAsync(fixture, fixture.TenantId, "U-1", "D-DESK")).Value!.Id);
        Assert.Equal(1, CountRows(fixture, fixture.TenantId, "U-1", "D-PHONE"));
        Assert.Equal(1, CountRows(fixture, fixture.TenantId, "U-1", "D-DESK"));
    }

    [Fact]
    public async Task DifferentTenants_AreIsolated()
    {
        using var fixture = Fixture();
        await fixture.MigrateAsync();
        var signer = fixture.Services.GetRequiredService<ICapabilitySnapshotSigner>();

        var local = Sign(signer, fixture.TenantId, "U-1", "D-1", Base, PermissionCodes.PatientsRead);
        var other = Sign(signer, "T-OTHER", "U-1", "D-1", Base, PermissionCodes.SalesRead);
        await ReplaceAsync(fixture, local);
        await ReplaceAsync(fixture, other);

        Assert.Equal(local.Id, (await GetAsync(fixture, fixture.TenantId, "U-1", "D-1")).Value!.Id);
        Assert.Equal(other.Id, (await GetAsync(fixture, "T-OTHER", "U-1", "D-1")).Value!.Id);
        Assert.Null((await GetAsync(fixture, "T-NEVER", "U-1", "D-1")).Value);
    }

    [Fact]
    public async Task Revoke_MarksRevoked_AndTheSignatureRemainsValid()
    {
        using var fixture = Fixture();
        await fixture.MigrateAsync();
        var signer = fixture.Services.GetRequiredService<ICapabilitySnapshotSigner>();
        var snapshot = Sign(signer, fixture.TenantId, "U-1", "D-1", Base,
            PermissionCodes.PatientsRead, PermissionCodes.PatientsWrite);
        await ReplaceAsync(fixture, snapshot);

        using (var scope = fixture.CreateScope())
        {
            var revoked = await scope.ServiceProvider.GetRequiredService<ICapabilityStore>()
                .RevokeAsync(fixture.TenantId, "U-1", "D-1", Base.AddHours(1));
            Assert.True(revoked.IsSuccess, revoked.Detail);
        }

        var loaded = await GetAsync(fixture, fixture.TenantId, "U-1", "D-1");
        Assert.NotNull(loaded.Value!.RevokedAtUtc);
        Assert.True(signer.Verify(loaded.Value).IsSuccess); // the revoked state is re-signed, still valid.
    }

    [Fact]
    public async Task Revoke_WhenAbsent_IsSuccess()
    {
        using var fixture = Fixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var revoked = await scope.ServiceProvider.GetRequiredService<ICapabilityStore>()
            .RevokeAsync(fixture.TenantId, "U-none", "D-1", Base);

        Assert.True(revoked.IsSuccess);
    }
}
