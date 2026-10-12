using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Server.Auth;
using Microsoft.AspNetCore.Identity;

namespace Animora.Server.Tests.Identity;

/// <summary>Controllable clock for deterministic lockout / expiry tests.</summary>
internal sealed class TestClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>In-memory <see cref="IIdentityRepository"/>: aggregates are held by reference, so mutations "persist".</summary>
internal sealed class FakeIdentityRepository : IIdentityRepository
{
    public List<User> Users { get; } = [];

    public List<StaffMembership> Memberships { get; } = [];

    public List<Device> Devices { get; } = [];

    public Task<User?> FindUserByNameAsync(string normalizedUserName, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.UserName == normalizedUserName));

    public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == userId));

    public Task<StaffMembership?> FindActiveMembershipAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.UserId == userId && m.IsActive));

    public Task<Device?> FindDeviceByInstallationAsync(string userId, string installationIdHash, CancellationToken ct = default) =>
        Task.FromResult(Devices.FirstOrDefault(d => d.UserId == userId && d.InstallationIdHash == installationIdHash));

    public Task<Device?> FindDeviceByIdAsync(string deviceId, CancellationToken ct = default) =>
        Task.FromResult(Devices.FirstOrDefault(d => d.Id == deviceId));

    public Task<StaffMembership?> FindMembershipByIdAsync(string membershipId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.Id == membershipId));

    public Task<IReadOnlyList<(User User, StaffMembership Membership)>> ListStaffAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(User User, StaffMembership Membership)>>(Memberships
            .Where(m => m.TenantId == tenantId)
            .Join(Users.Where(u => u.TenantId == tenantId), m => m.UserId, u => u.Id, (m, u) => (u, m))
            .ToArray());

    public Task<IReadOnlyList<Device>> ListDevicesAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Device>>(Devices.Where(d => d.TenantId == tenantId).ToArray());

    public void AddDevice(Device device) => Devices.Add(device);

    public void AddUser(User user) => Users.Add(user);

    public void AddMembership(StaffMembership membership) => Memberships.Add(membership);

    public Task<Result> SaveAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());
}

/// <summary>In-memory <see cref="IRefreshTokenStore"/>.</summary>
internal sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    public List<RefreshToken> Rows { get; } = [];

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.TokenHash == tokenHash));

    public void Add(RefreshToken row) => Rows.Add(row);

    public Task<bool> TryRotateAsync(string oldId, RefreshToken successor, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        RefreshToken? old = Rows.FirstOrDefault(r => r.Id == oldId);
        if (old is null || old.RotatedToId is not null || old.RevokedUtc is not null)
        {
            return Task.FromResult(false);
        }

        old.MarkRotated(successor.Id, nowUtc);
        Rows.Add(successor);
        return Task.FromResult(true);
    }

    public Task<int> RevokeFamilyAsync(string familyId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        int count = 0;
        foreach (RefreshToken row in Rows.Where(r => r.FamilyId == familyId && r.RevokedUtc is null))
        {
            row.Revoke(nowUtc);
            count++;
        }

        return Task.FromResult(count);
    }

    public Task<int> RevokeDeviceTokensAsync(string userId, string deviceId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        int count = 0;
        foreach (RefreshToken row in Rows.Where(r => r.UserId == userId && r.DeviceId == deviceId && r.RevokedUtc is null))
        {
            row.Revoke(nowUtc);
            count++;
        }

        return Task.FromResult(count);
    }
}

internal sealed class FakeAuditWriter : ISecurityAuditWriter
{
    public List<SecurityAuditEntry> Entries { get; } = [];

    public Task<Result> WriteAsync(SecurityAuditEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.FromResult(Result.Ok());
    }
}

internal sealed class FakeTenantDirectory : ITenantDirectory
{
    public List<TenantDirectoryEntry> Tenants { get; } = [];

    public Task<Result<TenantDirectoryEntry>> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        TenantDirectoryEntry? entry = Tenants.FirstOrDefault(t => t.Code == TenantDirectory.NormalizeCode(code));
        return Task.FromResult(entry is null
            ? Result<TenantDirectoryEntry>.Fail(ErrorCodes.CredentialsInvalid)
            : Result<TenantDirectoryEntry>.Ok(entry));
    }

    public Task<Result<TenantDirectoryEntry>> FindByIdAsync(string tenantId, CancellationToken ct = default)
    {
        TenantDirectoryEntry? entry = Tenants.FirstOrDefault(t => t.Id == tenantId);
        return Task.FromResult(entry is null
            ? Result<TenantDirectoryEntry>.Fail(ErrorCodes.CredentialsInvalid)
            : Result<TenantDirectoryEntry>.Ok(entry));
    }
}

/// <summary>Deterministic fake access-token issuer (no Data Protection needed).</summary>
internal sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(AccessTokenClaims claims) =>
        new("access-" + claims.UserId + "-" + claims.DeviceId, 900);
}

/// <summary>A fully wired <see cref="LoginService"/> over in-memory fakes, seeded with one tenant + owner.</summary>
internal sealed class AuthHarness
{
    public const string TenantCode = "clinic";
    public const string UserName = "alice";
    public const string Password = "Sup3r-secret-pw";
    public const string Installation = "install-hash-1";

    public AuthHarness()
    {
        Clock = new TestClock(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));
        TenantId = IdGenerator.NewId();
        Tenants.Tenants.Add(new TenantDirectoryEntry(TenantId, TenantCode, "Clinic", TenantStatus.Active));

        User = User.Create(TenantId, UserName, "Alice", Clock);
        User.SetPasswordHash(Hasher.HashPassword(ServerUserStore.ToAdapter(User), Password));
        Repository.Users.Add(User);
        Repository.Memberships.Add(StaffMembership.Create(TenantId, User.Id, StaffRole.Manager, Clock));

        Refresh = new RefreshTokenService(TokenStore, Clock);
        Service = new LoginService(Tenants, TenantContext, Repository, Refresh, Audit, Hasher, new FakeAccessTokenIssuer(), Clock);
    }

    public TestClock Clock { get; }

    public string TenantId { get; }

    public User User { get; }

    public PasswordHasher<ServerIdentityUser> Hasher { get; } = new();

    public FakeIdentityRepository Repository { get; } = new();

    public FakeRefreshTokenStore TokenStore { get; } = new();

    public FakeAuditWriter Audit { get; } = new();

    public FakeTenantDirectory Tenants { get; } = new();

    public AuthTenantContext TenantContext { get; } = new();

    public RefreshTokenService Refresh { get; }

    public LoginService Service { get; }

    public LoginRequest Login(string? password = null, string? tenantCode = null, string? userName = null, string? installation = null) =>
        new(tenantCode ?? TenantCode, userName ?? UserName, password ?? Password, installation ?? Installation);

    /// <summary>A fresh service + scope-bound tenant context sharing the same stores (a new HTTP request).</summary>
    public LoginService NewRequestScope() =>
        new(Tenants, new AuthTenantContext(), Repository, Refresh, Audit, Hasher, new FakeAccessTokenIssuer(), Clock);
}
