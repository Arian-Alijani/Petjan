using System.Net;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Replica;
using Microsoft.EntityFrameworkCore;
using static Animora.Sync.Tests.ReplicaTestSupport;
using Animora.Application.Security;
using Animora.Client.Services.Identity;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Identity;
using Animora.Server.Licensing.Verification;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G06: online login / offline credential mirror / device revocation, over a real migrated SQLite file and a wire-faithful fake server.</summary>
public sealed class OnlineAuthClientTests
{
    private static Task<Result<OnlineLoginOutcome>> LoginAsync(OnlineStack s, string password = FakeIdentityServer.Password) =>
        s.Client.LoginAsync(FakeIdentityServer.TenantCode, FakeIdentityServer.UserName, password);

    [Fact]
    public async Task Login200_PersistsSession_AndSealsEntitlement()
    {
        using var stack = await OnlineStack.CreateAsync();

        var login = await LoginAsync(stack);

        Assert.True(login.IsSuccess, login.Detail);
        Assert.False(login.Value!.IsOffline);
        Assert.Equal(stack.Server.UserId, login.Value.UserId);

        var session = (await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value;
        Assert.NotNull(session);
        Assert.Equal(stack.Server.LastAccessToken, session!.AccessToken);
        Assert.Equal("STAMP-1", session.SecurityStamp);

        var sealedValue = (await stack.Services.GetRequiredService<ISealedEntitlementStore>().ReadAsync()).Value;
        Assert.NotNull(sealedValue);
        Assert.Equal(stack.Server.TenantId, sealedValue!.TenantId);
        Assert.True(LicenseEnvelope.Parse(sealedValue.LicenseToken).IsSuccess);

        // The device was registered, the capability snapshot mirrored, and the licence activated — in that online order.
        var paths = stack.Server.Handler.Requests.Select(r => r.Path).ToList();
        Assert.Contains("auth/device/register", paths);
        Assert.Contains("auth/capabilities", paths);
        Assert.Contains("licensing/activate", paths);
        Assert.True((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value is not null);

        // Access/refresh tokens never sit in SQLite as plaintext.
        using var db = stack.Fixture.CreateContext(stack.Server.TenantId);
        var row = db.Set<Animora.Persistence.Sqlite.Entities.ClientSessionRow>().Single();
        Assert.DoesNotContain(stack.Server.LastAccessToken, row.AccessToken, StringComparison.Ordinal);
        Assert.NotNull(row.OfflineCredentialVerifier);
    }

    [Fact]
    public async Task SecondLogin_RenewsInsteadOfActivating()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        Assert.True((await LoginAsync(stack)).IsSuccess);

        Assert.Equal(1, stack.Server.Handler.Count("licensing/activate"));
        Assert.Equal(1, stack.Server.Handler.Count("licensing/renew"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "CREDENTIALS_INVALID")]
    [InlineData((HttpStatusCode)423, "USER_LOCKED_OUT")]
    [InlineData(HttpStatusCode.Forbidden, "DEVICE_REVOKED")]
    public async Task DefinitiveServerRefusal_IsFinal_NoOfflineFallback(HttpStatusCode status, string code)
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess); // a mirror exists, so a fallback WOULD be possible
        stack.Server.LoginFailure = (status, code);

        var login = await LoginAsync(stack);

        Assert.True(login.IsFailure);
        Assert.Equal(code, login.Error.Code);
    }

    [Fact]
    public async Task EmptyInput_FailsValidation_WithoutAnyRequest()
    {
        using var stack = await OnlineStack.CreateAsync();

        var login = await stack.Client.LoginAsync("", FakeIdentityServer.UserName, FakeIdentityServer.Password);

        Assert.Equal(ErrorCodes.ValidationFailed, login.Error);
        Assert.Empty(stack.Server.Handler.Requests);
    }

    [Fact]
    public async Task ServerUnreachable_UsesCredentialMirror_WhenWindowsValid()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;
        stack.Fixture.Clock.Now = stack.Server.Now.AddDays(2); // inside both the capability window (7d) and the licence

        var offline = await LoginAsync(stack);

        Assert.True(offline.IsSuccess, offline.Detail);
        Assert.True(offline.Value!.IsOffline);
        Assert.Equal(stack.Server.UserId, offline.Value.UserId);
    }

    [Fact]
    public async Task ServerUnreachable_WrongPassword_FailsClosed_CredentialMismatch()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;

        var offline = await LoginAsync(stack, "not-the-password");

        Assert.Equal(ErrorCodes.OfflineCredentialMismatch, offline.Error);
    }

    [Fact]
    public async Task ServerUnreachable_NeverLoggedInBefore_IsNetworkUnavailable()
    {
        using var stack = await OnlineStack.CreateAsync();
        stack.Server.Handler.Unreachable = true;

        var offline = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.NetworkUnavailable, offline.Error);
    }

    [Fact]
    public async Task ServerUnreachable_CapabilityWindowElapsed_FailsClosed_SessionUnavailable()
    {
        using var stack = await OnlineStack.CreateAsync(capabilityWindow: TimeSpan.FromHours(12));
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;
        stack.Fixture.Clock.Now = stack.Server.Now.AddDays(3); // capability snapshot (12h) is long gone, licence still fine

        var offline = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.OfflineSessionUnavailable, offline.Error);
        Assert.Equal("capability_expired", offline.Detail);
    }

    [Fact]
    public async Task ServerUnreachable_BackwardClock_CannotExtendOfflineWindow()
    {
        using var stack = await OnlineStack.CreateAsync(capabilityWindow: TimeSpan.FromDays(1));
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;

        // The device has already observed a time past the capability window; the OS clock is then wound back to the login instant.
        stack.Monotonic.Observe(stack.Server.Now.AddDays(5));
        stack.Fixture.Clock.Now = stack.Server.Now;

        var offline = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.OfflineSessionUnavailable, offline.Error);
    }

    [Fact]
    public async Task DeviceRevoked_OnRegister_AbortsLogin_AndWipesOfflineAuthority()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess); // a healthy first login builds the offline authority
        Assert.NotNull((await stack.Services.GetRequiredService<ISealedEntitlementStore>().ReadAsync()).Value);

        stack.Server.DeviceRegisterFailureCode = "DEVICE_REVOKED";
        var login = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.DeviceRevoked, login.Error);
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        Assert.Null((await stack.Services.GetRequiredService<ISealedEntitlementStore>().ReadAsync()).Value);
        Assert.Null((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value);

        var snapshot = (await stack.Services.GetRequiredService<ICapabilityStore>()
            .GetAsync(stack.Server.TenantId, stack.Server.UserId, stack.Server.DeviceId)).Value;
        Assert.True(snapshot is null || snapshot.RevokedAtUtc is not null, "capability snapshot must be revoked or gone");

        // And the wiped installation can no longer start offline.
        stack.Server.Handler.Unreachable = true;
        var offline = await LoginAsync(stack);
        Assert.True(offline.IsFailure);
        Assert.False(offline.Value?.IsOffline ?? false);
    }

    [Fact]
    public async Task PendingDevice_OnRegister_FailsClosedWithDistinctCode_AndWritesNoOfflineAuthority()
    {
        using var stack = await OnlineStack.CreateAsync();
        stack.Server.DeviceRegisterStatus = "Pending";

        var login = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.DevicePendingApproval, login.Error);
        Assert.NotEqual(ErrorCodes.DeviceRevoked, login.Error);
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        Assert.Null((await stack.Services.GetRequiredService<ISealedEntitlementStore>().ReadAsync()).Value);
        Assert.Null((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value);

        // Without ever having been approved there is nothing to start offline from either.
        stack.Server.Handler.Unreachable = true;
        Assert.True((await LoginAsync(stack)).IsFailure);
    }

    [Fact]
    public async Task Logout_ClearsSession_ButKeepsOfflineMirror()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);

        var result = await stack.Client.LogoutAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, stack.Server.Handler.Count("auth/logout"));
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        Assert.NotNull((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value);
    }
    private static async Task AddSyncedOwnerAsync(OnlineStack stack)
    {
        var record = new SyncRecord("Owner", "OWNER-A", 'U', "v1", FeedFixtures.Now, false,
            JsonDocument.Parse("""{"fullName":"Clinic A owner","phonePrimary":"09120000000","gender":1,"registeredUtc":"2026-10-01T08:00:00Z","isBlocked":false}""").RootElement.Clone());
        var applied = await ApplyAsync(NewStore(stack.Fixture), Batch("tenant-batch", 1, 0, 10), Page("tenant-batch", 0, 1, record));
        Assert.True(applied.IsSuccess, applied.Detail);
    }

    [Fact]
    public async Task TenantBind_AdoptsServerTenant_AndSurvivesRestartWithoutDefaultSeed()
    {
        using var stack = await OnlineStack.CreateAsync();
        var installation = stack.Services.GetRequiredService<LocalInstallationState>().Installation!;
        Assert.True((await LoginAsync(stack)).IsSuccess);
        Assert.Equal(stack.Server.TenantId, stack.Services.GetRequiredService<LocalInstallationState>().TenantId);
        using var restarted = stack.Fixture.BuildProvider();
        var result = await restarted.GetRequiredService<ILocalStoreInitializer>().InitializeAsync(default);
        Assert.True(result.IsSuccess, result.Detail);
        var state = restarted.GetRequiredService<LocalInstallationState>();
        Assert.Equal(stack.Server.TenantId, state.TenantId);
        Assert.Equal(installation.InstallationId, state.Installation!.InstallationId);
        Assert.Equal(installation.MachineFingerprintHash, state.Installation.MachineFingerprintHash);
        using var scope = restarted.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(await db.Set<Tenant>().IgnoreQueryFilters().AnyAsync(x => x.Id == Tenant.LocalDefaultId));
        Assert.Equal(Enum.GetValues<StaffRole>().Length, await db.Set<RoleDefinition>().CountAsync());
    }

    [Fact]
    public async Task TenantSwitch_WipesTypedReplicaAndCursorRows_ThenCanReturnToOriginalClinic()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        var tenantA = stack.Server.TenantId;
        await AddSyncedOwnerAsync(stack);
        Assert.True((await stack.Client.LogoutAsync()).IsSuccess);
        stack.Server.TenantId = IdGenerator.NewId();
        var loginB = await LoginAsync(stack);
        Assert.True(loginB.IsSuccess, loginB.Detail);
        using (var scope = stack.Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.False(await db.Set<Owner>().IgnoreQueryFilters().AnyAsync());
            Assert.False(await db.Set<ReplicaBase>().AnyAsync());
            Assert.False(await db.Set<ReplicaCheckpoint>().AnyAsync());
            Assert.False(await db.Set<ClientSessionRow>().AnyAsync(x => x.TenantId == tenantA));
            Assert.False(await db.Set<RoleDefinition>().IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantA));
        }
        Assert.Equal(stack.Server.TenantId, stack.Services.GetRequiredService<LocalInstallationState>().TenantId);
        Assert.True((await stack.Client.LogoutAsync()).IsSuccess);
        stack.Server.TenantId = tenantA;
        Assert.True((await LoginAsync(stack)).IsSuccess);
        await AddSyncedOwnerAsync(stack); // fresh server hydration, not resurrection of old disk rows
        using var read = stack.Fixture.CreateScope();
        Assert.Equal(tenantA, (await read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>()
            .Set<Owner>().SingleAsync()).TenantId);
    }

    [Fact]
    public async Task TenantSwitch_WithUnsettledWork_IsRefusedWithoutDiscardingAnyIntent()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        var tenantA = stack.Server.TenantId;
        await AddSyncedOwnerAsync(stack);
        using (var scope = stack.Fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var command = CommandEnvelope.Create(IdGenerator.NewId(), tenantA, "U1", "D1", "patients.owner.update", "Owner",
                "OWNER-A", "{}", "fp", FeedFixtures.Now, 1);
            Assert.True((await uow.ExecuteAsync(async ct => await journal.EnqueueAsync(command, ct))).IsSuccess);
        }
        // A same-tenant re-login is a genuine no-op, even with unsettled work.
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.TenantId = IdGenerator.NewId();
        var refused = await LoginAsync(stack);
        Assert.Equal(ErrorCodes.SyncPendingChanges, refused.Error);
        Assert.Equal(tenantA, stack.Services.GetRequiredService<LocalInstallationState>().TenantId);
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        using var read = stack.Fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.True(await db.Set<Owner>().AnyAsync(x => x.Id == "OWNER-A"));
        Assert.Single(await db.Set<PendingCommand>().ToListAsync());
    }

    [Fact]
    public async Task TenantSwitch_FailedCommit_RollsBackWipeAndCachedBinding()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        var tenantA = stack.Server.TenantId;
        await AddSyncedOwnerAsync(stack);
        using (var scope = stack.Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var table = db.Model.FindEntityType(typeof(LocalInstallation))!.GetTableName();
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"CREATE TRIGGER fail_tenant_bind BEFORE UPDATE ON \"{table}\" BEGIN SELECT RAISE(ABORT, 'forced'); END;";
            await command.ExecuteNonQueryAsync();
        }
        stack.Server.TenantId = IdGenerator.NewId();
        var refused = await LoginAsync(stack);
        Assert.True(refused.IsFailure);
        Assert.Equal(tenantA, stack.Services.GetRequiredService<LocalInstallationState>().TenantId);
        using var read = stack.Fixture.CreateScope();
        var dbRead = read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.True(await dbRead.Set<Owner>().AnyAsync(x => x.Id == "OWNER-A"));
        Assert.True(await dbRead.Set<ReplicaBase>().AnyAsync());
        Assert.Equal(tenantA, (await dbRead.Set<LocalInstallation>().SingleAsync()).TenantId);
    }

    [Fact]
    public async Task OfflineLogin_WrongInstallationTenant_FailsClosed()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        using (var scope = stack.Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var table = db.Model.FindEntityType(typeof(LocalInstallation))!.GetTableName();
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"UPDATE \"{table}\" SET \"TenantId\" = @tenant";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tenant";
            parameter.Value = IdGenerator.NewId();
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
        }
        using var restarted = stack.Fixture.BuildProvider();
        Assert.True((await restarted.GetRequiredService<ILocalStoreInitializer>().InitializeAsync(default)).IsSuccess);
        stack.Server.Handler.Unreachable = true;
        var offline = await restarted.GetRequiredService<IOnlineAuthClient>()
            .LoginAsync(FakeIdentityServer.TenantCode, FakeIdentityServer.UserName, FakeIdentityServer.Password);
        Assert.Equal(ErrorCodes.OfflineSessionUnavailable, offline.Error);
        Assert.Equal("wrong_local_tenant", offline.Detail);
    }

    [Fact]
    public async Task Logout_Offline_ClearsBearerAuthorityAndAllowsSameClinicOfflineRelogin()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;
        Assert.True((await stack.Client.LogoutAsync()).IsSuccess);
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        var offline = await LoginAsync(stack);
        Assert.True(offline.IsSuccess, offline.Detail);
        Assert.True(offline.Value!.IsOffline);
    }

}
