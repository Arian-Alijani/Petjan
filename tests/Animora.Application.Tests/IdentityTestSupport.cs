using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Identity;
using Animora.Application.UseCases.Profile;
using Animora.Application.UseCases.Dashboard;
using Animora.Application.UseCases.Visits;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Maintenance;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Tests;

// Shared B03 identity test doubles (B03c). Deterministic: injected mutable clock, no wall-clock, no sleeps.

internal sealed class MutableClock(DateTimeOffset start) : IClock
{
    public static readonly DateTimeOffset Start = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    public MutableClock() : this(Start)
    {
    }

    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Cheap, transparent hasher that counts every verification (no crypto — handler tests only).</summary>
internal sealed class CountingHasher : IPasswordHasher
{
    private const string Prefix = "TEST$";

    public int HashCount { get; private set; }

    public int VerifyCount { get; private set; }

    public string Hash(string password)
    {
        HashCount++;
        return Prefix + password;
    }

    public PasswordVerificationOutcome Verify(string hash, string password)
    {
        VerifyCount++;
        return string.Equals(hash, Prefix + password, StringComparison.Ordinal)
            ? PasswordVerificationOutcome.Success
            : PasswordVerificationOutcome.Failed;
    }
}

internal sealed class FakeIdentityRepository : IIdentityRepository
{
    public List<User> Users { get; } = [];

    public Tenant Tenant { get; } = Tenant.CreateLocalDefault("Default clinic");

    public Task<User?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public async Task<User> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("missing user");

    public Task<User?> FindByUsernameAsync(string tenantId, string userName, CancellationToken ct = default)
    {
        var normalized = userName.Trim().ToLowerInvariant();
        return Task.FromResult(Users.FirstOrDefault(u => u.TenantId == tenantId && u.UserName == normalized));
    }

    public Task<bool> AnyUserAsync(CancellationToken ct = default) => Task.FromResult(Users.Count > 0);

    public Task<Tenant?> FindTenantAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(tenantId == Tenant.Id ? Tenant : null);

    public void Add(User entity) => Users.Add(entity);

    public void AddRange(IEnumerable<User> entities) => Users.AddRange(entities);
}

internal sealed class FakeMembershipRepository : IStaffMembershipRepository
{
    public List<StaffMembership> Memberships { get; } = [];

    public Task<StaffMembership?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.Id == id));

    public Task<StaffMembership?> FindActiveAsync(string tenantId, string userId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.TenantId == tenantId && m.UserId == userId && m.IsActive));

    public void Add(StaffMembership entity) => Memberships.Add(entity);
}

internal sealed class FakeDeviceRepository : IDeviceRepository
{
    public List<Device> Devices { get; } = [];

    public Task<Device?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Devices.FirstOrDefault(d => d.Id == id));

    public void Add(Device entity) => Devices.Add(entity);
}

internal sealed class NullRoleRepository : IRoleDefinitionRepository
{
    public Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default) =>
        Task.FromResult<RoleDefinition?>(null);
}

/// <summary>B04b compile fix: stored system role definitions so snapshot issuance (fail-closed on a missing role) succeeds.</summary>
internal sealed class SeededRoleRepository(IClock clock) : IRoleDefinitionRepository
{
    public Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default) =>
        Task.FromResult<RoleDefinition?>(RoleDefinition.CreateSystem(tenantId, role, clock));
}

/// <summary>B04b compile fix: in-memory capability store (one row per tenant/user/device).</summary>
internal sealed class InMemoryCapabilityStore : ICapabilityStore
{
    public Dictionary<(string, string, string), StoredCapabilitySnapshot> Rows { get; } = [];

    public Task<Result<StoredCapabilitySnapshot?>> GetAsync(string tenantId, string userId, string deviceId,
        CancellationToken ct = default) =>
        Task.FromResult(Result<StoredCapabilitySnapshot?>.Ok(Rows.GetValueOrDefault((tenantId, userId, deviceId))));

    public Task<Result> ReplaceAsync(StoredCapabilitySnapshot snapshot, CancellationToken ct = default)
    {
        Rows[(snapshot.TenantId, snapshot.UserId, snapshot.DeviceId)] = snapshot;
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> RevokeAsync(string tenantId, string userId, string deviceId, DateTimeOffset revokedAtUtc,
        CancellationToken ct = default) => Task.FromResult(Result.Ok());
}

/// <summary>B04b compile fix: deterministic non-cryptographic signer for handler-level tests.</summary>
internal sealed class TestCapabilitySigner : ICapabilitySnapshotSigner
{
    public Result<string> Sign(StoredCapabilitySnapshot snapshot)
    {
        var canonical = CapabilitySnapshotCodec.Canonicalize(snapshot);
        return canonical.IsFailure
            ? Result<string>.Fail(canonical.Error, canonical.Detail)
            : Result<string>.Ok(Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(canonical.Value!)));
    }

    public Result Verify(StoredCapabilitySnapshot snapshot)
    {
        var expected = Sign(snapshot);
        return expected.IsSuccess && expected.Value == snapshot.SnapshotSignature
            ? Result.Ok()
            : Result.Fail(ErrorCodes.CapabilitySnapshotTampered);
    }
}

internal sealed class RecordingAttemptLog : ILoginAttemptLog
{
    public List<string> Outcomes { get; } = [];

    public void Append(string usernameAttempted, string outcome, string deviceId, DateTimeOffset nowUtc) =>
        Outcomes.Add(outcome);
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<Result> SaveAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.FromResult(Result.Ok());
    }

    // B04b: success paths now commit through ExecuteAsync; one successful unit counts as one save (mirrors the real UoW).
    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
    {
        var outcome = await work(ct);
        if (outcome.IsSuccess) Saves++;
        return outcome;
    }
}

/// <summary>Records session starts; the snapshot expiry follows the injected clock.</summary>
internal sealed class RecordingSessionService(IClock clock) : ISessionService
{
    public List<string> StartedFor { get; } = [];

    public Task<Result<SessionSnapshot>> RestoreAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<SessionSnapshot>.Ok(SessionSnapshot.SignedOut(isFirstRun: StartedFor.Count == 0)));

    public Task<Result<SessionSnapshot>> StartAsync(string userId, string deviceId, CancellationToken ct = default)
    {
        StartedFor.Add(userId);
        return Task.FromResult(Result<SessionSnapshot>.Ok(
            new SessionSnapshot(userId, userId, StaffRole.Manager, false, false, clock.UtcNow.AddHours(12))));
    }

    public Task<Result> TouchAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());

    public Task<Result> LockAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());

    public Task<Result<SessionSnapshot>> UnlockAsync(string password, CancellationToken ct = default) =>
        Task.FromResult(Result<SessionSnapshot>.Fail(ErrorCodes.NotFound));

    public Task<Result> EndAsync(string reason, CancellationToken ct = default) => Task.FromResult(Result.Ok());
}

/// <summary>In-memory identity world for handler-level tests (sign-in, first-run, password policy).</summary>
internal sealed class IdentityWorld
{
    public MutableClock Clock { get; } = new();

    public IPasswordHasher Hasher { get; }

    public FakeIdentityRepository Users { get; } = new();

    public FakeMembershipRepository Memberships { get; } = new();

    public FakeDeviceRepository Devices { get; } = new();

    public RecordingAttemptLog Attempts { get; } = new();

    public CountingUnitOfWork UnitOfWork { get; } = new();

    public SessionContextCache Cache { get; } = new();

    public RecordingSessionService Sessions { get; }

    public InMemoryCapabilityStore Capabilities { get; } = new();

    public IdentityWorld(IPasswordHasher? hasher = null)
    {
        Hasher = hasher ?? new CountingHasher();
        Sessions = new RecordingSessionService(Clock);
    }

    public CountingHasher Counting => (CountingHasher)Hasher;

    public CommandContext Context() => new(
        Tenant.LocalDefaultId, ICurrentUserProvider.AnonymousUserId, "D-TEST", Domain.Primitives.IdGenerator.NewId(),
        Domain.Primitives.IdGenerator.NewId(), null, null, null, Clock.UtcNow);

    /// <summary>Adds an active user with an active membership and the given stored hash.</summary>
    public User AddUser(string username, string storedHash, StaffRole role = StaffRole.Manager)
    {
        var user = User.Create(Tenant.LocalDefaultId, username, "Owner " + username, Clock);
        user.SetPasswordHash(storedHash);
        Users.Add(user);
        Memberships.Add(StaffMembership.Create(Tenant.LocalDefaultId, user.Id, role, Clock));
        return user;
    }

    public SignInCommandHandler SignIn() =>
        new(Users, Memberships, new NullRoleRepository(), Attempts, Hasher, Issuer(), Sessions, Cache, UnitOfWork, Clock);

    public CompleteFirstRunSetupCommandHandler FirstRun() =>
        new(Users, Memberships, Devices, new NullRoleRepository(), Attempts, Hasher, Issuer(), Sessions, Cache, UnitOfWork, Clock);

    private CapabilitySnapshotIssuer Issuer() =>
        new(new SeededRoleRepository(Clock), Capabilities, new TestCapabilitySigner(), Clock);
}

internal sealed class FixedLocalTenant : ILocalTenantContext
{
    public string TenantId => Tenant.LocalDefaultId;
}

/// <summary>
/// Real local stack (store + infrastructure + identity use cases) over a temporary <b>file</b> database —
/// never <c>:memory:</c>. <see cref="Restart"/> builds a fresh provider on the same file (simulated crash).
/// </summary>
internal sealed class LocalIdentityHost : IAsyncDisposable
{
    private readonly string _directory;
    private ServiceProvider _services;

    public LocalIdentityHost()
    {
        _directory = Path.Combine(Path.GetTempPath(), "animora-b03c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "animora.db");
        _services = Build();
    }

    public string DatabasePath { get; }

    public MutableClock Clock { get; } = new();

    public CountingHasher Hasher { get; } = new();

    public IServiceProvider Services => _services;

    public SessionContextCache Cache => _services.GetRequiredService<SessionContextCache>();

    public async Task InitializeAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ILocalStoreInitializer>().InitializeAsync(CancellationToken.None);
        if (result.IsFailure) throw new InvalidOperationException($"{result.Error.Code}: {result.Detail}");
    }

    /// <summary>Drops the current provider (and its in-memory state) and starts over on the same database file.</summary>
    public async Task RestartAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _services = Build();
        await InitializeAsync();
    }

    public async Task<Result<TResult>> SendAnonymousAsync<TResult>(ICommand<TResult> command)
    {
        await using var scope = _services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildAnonymousCommandContext();
        if (context.IsFailure) return Result<TResult>.Fail(context.Error, context.Detail);
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
    }

    public async Task<Result<TResult>> SendAsUserAsync<TResult>(ICommand<TResult> command)
    {
        await using var scope = _services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(null, null, null, CancellationToken.None);
        if (context.IsFailure) return Result<TResult>.Fail(context.Error, context.Detail);
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
    }

    public async Task<Result<SessionSnapshot>> RestoreAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new CurrentSessionQuery(), provider.BuildAnonymousQueryContext());
    }

    public async Task<Result<CommandContext>> BuildUserContextAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>()
            .BuildCommandContextAsync(null, null, null, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp file is not a test failure.
        }
    }

    private ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IPasswordHasher>(Hasher);
        services.AddAnimoraLocalInfrastructure(o =>
        {
            o.DatabasePath = DatabasePath;
            // C01 sample data is on by default for fresh installs; it also seeds sample staff users, which
            // would make CompleteFirstRunSetupCommand fail (a user already exists). An identity host wants a
            // clean store, and it needs the dev key file because the sandbox has no OS keystore.
            o.SeedSampleData = false;
            o.AllowInsecureKeyFallback = true;
        });
        services.AddScoped<ILocalTenantContext, FixedLocalTenant>();
        services.AddAnimoraApplication(b => b.AddIdentityUseCases().AddProfileUseCases().AddVisitsUseCases()
            .AddDashboardUseCases());
        return services.BuildServiceProvider();
    }
}
