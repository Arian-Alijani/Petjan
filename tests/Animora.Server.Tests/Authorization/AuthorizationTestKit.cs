using System.Security.Cryptography;
using Animora.Application.Ports;
using IIdentityRepository = Animora.Persistence.PostgreSql.Identity.IIdentityRepository;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Security;
using Animora.Server.Auth;
using Animora.Server.Authorization;
using Animora.Server.Signing;
using Animora.Server.Tests.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Server.Tests.Authorization;

/// <summary>In-memory stored RBAC: both Application ports over the same membership list the identity fake uses.</summary>
internal sealed class FakeRbac(List<StaffMembership> memberships) : IStaffMembershipRepository, IRoleDefinitionRepository
{
    public List<RoleDefinition> Roles { get; } = [];

    public Task<StaffMembership?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(memberships.FirstOrDefault(m => m.Id == id));

    public Task<StaffMembership?> FindActiveAsync(string tenantId, string userId, CancellationToken ct = default) =>
        Task.FromResult(memberships.FirstOrDefault(m => m.TenantId == tenantId && m.UserId == userId && m.IsActive));

    public void Add(StaffMembership entity) => memberships.Add(entity);

    public Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default) =>
        Task.FromResult(Roles.FirstOrDefault(r => r.TenantId == tenantId && r.Role == role));
}

/// <summary>Primary-key-only user lookup over the identity fake (what <c>UserLookup</c> does over PostgreSQL).</summary>
internal sealed class FakeUserLookup(FakeIdentityRepository repository) : IUserLookup
{
    public Task<UserLookupEntry?> FindByIdAsync(string userId, CancellationToken ct = default)
    {
        User? user = repository.Users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(user is null
            ? null
            : new UserLookupEntry(user.Id, user.TenantId, user.SecurityStamp, user.Status));
    }
}

/// <summary>A real ECDSA signer with an in-memory key row, standing in for the PostgreSQL-backed key store.</summary>
internal sealed class FakeSignerProvider : ISnapshotSignerProvider
{
    private readonly EcdsaSnapshotSigner _signer;

    public FakeSignerProvider(Animora.Domain.Time.IClock clock)
    {
        (byte[] publicKey, byte[] pkcs8) = EcdsaSnapshotSigner.GenerateKeyPair();
        PrivateKeyPkcs8 = pkcs8;
        ProtectedPrivateKey = RandomNumberGenerator.GetBytes(64);
        Key = SigningKey.Create(SigningKey.Purposes.Snapshot, EcdsaSnapshotSigner.AlgorithmName, publicKey, ProtectedPrivateKey, clock);
        _signer = EcdsaSnapshotSigner.FromPkcs8(Key.KeyId, pkcs8);
    }

    public SigningKey Key { get; }

    public byte[] PrivateKeyPkcs8 { get; }

    public byte[] ProtectedPrivateKey { get; }

    public Task<ISnapshotSigner> GetAsync(CancellationToken ct = default) => Task.FromResult<ISnapshotSigner>(_signer);

    public Task<IReadOnlyList<SigningKey>> ListPublicAsync(string purpose, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SigningKey>>(purpose == Key.Purpose ? [Key] : []);
}

/// <summary>An entitlement gate that always refuses (proves plane ordering).</summary>
internal sealed class DenyAllEntitlementGate : IServerEntitlementGate
{
    public ValueTask<Result> EvaluateAsync(TenantEntitlementContext context, CancellationToken ct = default) =>
        ValueTask.FromResult(Result.Fail(ErrorCodes.EntitlementRequired));
}

/// <summary>
/// The seeded server state of one test: tenant "clinic" with a Manager (alice) and a Receptionist (bob), each with an
/// active device, plus stored role definitions. Everything lives in memory and is shared by reference with the host.
/// </summary>
internal sealed class AuthzWorld
{
    public AuthzWorld()
    {
        Clock = new TestClock(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));
        TenantId = Animora.Domain.Primitives.IdGenerator.NewId();
        ForeignTenantId = Animora.Domain.Primitives.IdGenerator.NewId();
        Rbac = new FakeRbac(Repository.Memberships);
        Signer = new FakeSignerProvider(Clock);
        Tenants.Tenants.Add(new TenantDirectoryEntry(TenantId, "clinic", "Clinic", TenantStatus.Active));

        foreach (StaffRole role in Enum.GetValues<StaffRole>())
        {
            Rbac.Roles.Add(RoleDefinition.CreateSystem(TenantId, role, Clock));
        }

        Alice = AddUser("alice", StaffRole.Manager);
        Bob = AddUser("bob", StaffRole.Receptionist);
        AliceDevice = AddDevice(Alice, approve: true);
        BobDevice = AddDevice(Bob, approve: true);
    }

    public TestClock Clock { get; }

    public string TenantId { get; }

    public string ForeignTenantId { get; }

    public FakeIdentityRepository Repository { get; } = new();

    public FakeRbac Rbac { get; }

    public FakeAuditWriter Audit { get; } = new();

    public FakeTenantDirectory Tenants { get; } = new();

    public FakeSignerProvider Signer { get; }

    public User Alice { get; }

    public User Bob { get; }

    public Device AliceDevice { get; }

    public Device BobDevice { get; }

    public User AddUser(string name, StaffRole role)
    {
        User user = User.Create(TenantId, name, name, Clock);
        Repository.Users.Add(user);
        Repository.Memberships.Add(StaffMembership.Create(TenantId, user.Id, role, Clock));
        return user;
    }

    public Device AddDevice(User owner, bool approve)
    {
        Device device = Device.Register(TenantId, owner.Id, owner.UserName + "-pc", "install-" + Guid.NewGuid().ToString("N"), "windows", Clock);
        if (approve)
        {
            device.Approve();
        }

        Repository.Devices.Add(device);
        return device;
    }

    public StaffMembership MembershipOf(User user) => Repository.Memberships.First(m => m.UserId == user.Id);

    public void SetTenantStatus(TenantStatus status)
    {
        Tenants.Tenants.Clear();
        Tenants.Tenants.Add(new TenantDirectoryEntry(TenantId, "clinic", "Clinic", status));
    }
}

/// <summary>The real server pipeline (Program.cs) with every persistence seam swapped for the in-memory world.</summary>
internal sealed class AuthzHost : IAsyncDisposable
{
    private readonly string _dataRoot = Directory.CreateTempSubdirectory("animora-authz-test-").FullName;
    private readonly WebApplicationFactory<Program> _factory;

    public AuthzHost(AuthzWorld world, bool denyEntitlement = false)
    {
        World = world;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:DataRootPath"] = _dataRoot,
                    ["Server:RateLimits:Auth:PermitLimit"] = "100000",
                    ["Server:RateLimits:Auth:WindowSeconds"] = "60",
                }));

            builder.ConfigureServices(services =>
            {
                // Opaque bearer tokens without the PostgreSQL key ring.
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

                services.AddScoped<IUserLookup>(_ => new FakeUserLookup(world.Repository));
                services.AddScoped<IIdentityRepository>(_ => world.Repository);
                services.AddScoped<ITenantDirectory>(_ => world.Tenants);
                services.AddScoped<ISecurityAuditWriter>(_ => world.Audit);
                services.AddScoped<IStaffMembershipRepository>(_ => world.Rbac);
                services.AddScoped<IRoleDefinitionRepository>(_ => world.Rbac);
                services.AddScoped<ISnapshotSignerProvider>(_ => world.Signer);

                if (denyEntitlement)
                {
                    services.RemoveAll<IServerEntitlementGate>();
                    services.AddSingleton<IServerEntitlementGate, DenyAllEntitlementGate>();
                }
            });
        });
    }

    public AuthzWorld World { get; }

    /// <summary>Issues a real opaque access token. <paramref name="tenantClaim"/> / <paramref name="roleClaim"/> simulate a forged token.</summary>
    public string Token(User user, Device device, string? tenantClaim = null, string? roleClaim = null, string? stamp = null)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAccessTokenIssuer issuer = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>();
        return issuer.Issue(new AccessTokenClaims(
            user.Id,
            tenantClaim ?? user.TenantId,
            device.Id,
            stamp ?? user.SecurityStamp,
            roleClaim ?? StaffRole.Manager.ToString())).Token;
    }

    public HttpClient Client(string? token = null, string? tenantHeader = null)
    {
        HttpClient client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        if (tenantHeader is not null)
        {
            client.DefaultRequestHeaders.Add(TenantContextMiddleware.TenantHintHeader, tenantHeader);
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
