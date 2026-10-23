using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Security;
using Animora.Server.Auth;
using Animora.Server.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Animora.Server.Signing;
using Animora.Server.Tests.Identity;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>Real-PostgreSQL checks of S05 (skipped unless <c>ANIMORA_PG_TEST_CONNECTION</c> is set).</summary>
public sealed class AuthorizationPgTests(PgFixture pg) : IClassFixture<PgFixture>
{
    /// <summary>A container with the S04 + S05 composition over the migrated test database (the real server wiring).</summary>
    private static async Task<ServiceProvider> BuildAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(connectionString).Options;
        await using (var db = new AnimoraServerDbContext(options, new FixedTenantContext(null)))
        {
            await db.Database.MigrateAsync();
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName("Animora.Server.Tests");
        services.AddAnimoraPostgreSql(configuration);
        services.AddAnimoraIdentity(configuration);
        services.AddAnimoraAuthorization();
        return services.BuildServiceProvider(validateScopes: true);
    }

    [PgFact]
    public async Task SigningKeyMigration_Applies()
    {
        await using ServiceProvider provider = await BuildAsync(pg.ConnectionString);

        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='SigningKeys' ORDER BY column_name", conn);
        var columns = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        string[] expected = ["Algorithm", "CreatedUtc", "KeyId", "NotAfterUtc", "Purpose", "ProtectedPrivateKey", "PublicKeyDer", "RetiredUtc"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), columns.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("TenantId", columns); // global table: never tenant-scoped
        Assert.Equal("123_DeleteActorAudit", ServerSchemaVersion.ExpectedHead);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<SigningKeyStore>();
        ISnapshotSigner first = await store.GetAsync();
        ISnapshotSigner second = await store.GetAsync();
        Assert.Equal(first.KeyId, second.KeyId); // the first key is created once and then reused

        IReadOnlyList<SigningKey> listed = await store.ListPublicAsync(SigningKey.Purposes.Snapshot);
        Assert.Equal(first.KeyId, Assert.Single(listed).KeyId);
        byte[] payload = [1, 2, 3];
        Assert.True(EcdsaSnapshotSigner.VerifyWithPublicKey(listed[0].PublicKeyDer, payload, first.Sign(payload).Value!));
    }

    [PgFact]
    public async Task SigningKey_ConcurrentColdStart_YieldsOneActiveKey_AndSteadyStateIsCached()
    {
        // Two independent "instances" (own container, own signer cache) race on an empty key table.
        await using ServiceProvider a = await BuildAsync(pg.ConnectionString);
        await using ServiceProvider b = await BuildAsync(pg.ConnectionString);
        await using AsyncServiceScope sa = a.CreateAsyncScope();
        await using AsyncServiceScope sb = b.CreateAsyncScope();
        var storeA = sa.ServiceProvider.GetRequiredService<SigningKeyStore>();
        var storeB = sb.ServiceProvider.GetRequiredService<SigningKeyStore>();

        ISnapshotSigner[] signers = await Task.WhenAll(storeA.GetAsync(), storeB.GetAsync());

        Assert.Equal(signers[0].KeyId, signers[1].KeyId);
        IReadOnlyList<SigningKey> listed = await storeA.ListPublicAsync(SigningKey.Purposes.Snapshot);
        Assert.Single(listed);

        // Steady state: the very same signer instance is served from memory (no DB read / unprotect).
        Assert.Same(await storeA.GetAsync(), await storeA.GetAsync());
    }

    [PgFact]
    public async Task MembershipResolver_UsesOnlyPrimaryKey_NoCrossTenantEnumeration()
    {
        var identityHost = await IdentityPgHost.GetAsync(pg.ConnectionString);
        ProvisionedTenant a = await IdentityMigrationTests.Provision(identityHost, "authz-a", "owner-a");
        ProvisionedTenant b = await IdentityMigrationTests.Provision(identityHost, "authz-b", "owner-b");
        await using ServiceProvider provider = await BuildAsync(pg.ConnectionString);

        // The lookup contract exposes exactly one addressed read — there is no list / search surface to enumerate with.
        Assert.Single(typeof(IUserLookup).GetMethods());

        string stampA;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var lookup = scope.ServiceProvider.GetRequiredService<IUserLookup>();
            UserLookupEntry? entry = await lookup.FindByIdAsync(a.OwnerUserId);
            Assert.NotNull(entry);
            Assert.Equal(a.TenantId, entry!.TenantId); // resolved with NO tenant bound
            stampA = entry.SecurityStamp;
            Assert.Null(await lookup.FindByIdAsync("does-not-exist"));
            Assert.Null(await lookup.FindByIdAsync(string.Empty));
        }

        // Register + approve an owner-A device (tenant-scoped write).
        string deviceA;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<RequestTenantContext>().Bind(a.TenantId);
            var repository = scope.ServiceProvider.GetRequiredService<IIdentityRepository>();
            var clock = scope.ServiceProvider.GetRequiredService<Animora.Domain.Time.IClock>();
            Device device = Device.Register(a.TenantId, a.OwnerUserId, "pc", "install-authz-a", "windows", clock);
            device.Approve();
            repository.AddDevice(device);
            Assert.True((await repository.SaveAsync()).IsSuccess);
            deviceA = device.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<IMembershipResolver>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<Animora.Persistence.PostgreSql.Tenancy.ITenantContext>();
            Assert.Null(tenantContext.TenantId);

            Result<ResolvedMembership> ok = await resolver.ResolveAsync(a.OwnerUserId, deviceA, stampA);
            Assert.True(ok.IsSuccess, ok.Error.Code);
            Assert.Equal(a.TenantId, ok.Value!.TenantId);
            Assert.Equal(StaffRole.Manager, ok.Value.Role);
            Assert.Equal(a.TenantId, tenantContext.TenantId); // bound from the STORED user, not from any claim
        }

        // Wrong stamp / foreign user's device: indistinguishable closed failures, never another tenant's data.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<IMembershipResolver>();
            Result<ResolvedMembership> staleStamp = await resolver.ResolveAsync(a.OwnerUserId, deviceA, "old-stamp");
            Assert.Equal(ErrorCodes.SessionExpired.Code, staleStamp.Error.Code);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var lookup = scope.ServiceProvider.GetRequiredService<IUserLookup>();
            UserLookupEntry stampB = (await lookup.FindByIdAsync(b.OwnerUserId))!;
            var resolver = scope.ServiceProvider.GetRequiredService<IMembershipResolver>();
            Result<ResolvedMembership> crossDevice = await resolver.ResolveAsync(b.OwnerUserId, deviceA, stampB.SecurityStamp);
            Assert.True(crossDevice.IsFailure);
            Assert.Equal(ErrorCodes.DeviceRevoked.Code, crossDevice.Error.Code);
        }
    }
}
