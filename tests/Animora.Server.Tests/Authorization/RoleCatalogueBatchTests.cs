using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>SCALE-N1-01: the staff directory loads the role catalogue as one set, not one query per role.</summary>
public sealed class RoleCatalogueBatchTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class CountingRoles(IEnumerable<RoleDefinition> seeded) : IRoleDefinitionRepository
    {
        public int FindCalls;
        public Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default)
        {
            Interlocked.Increment(ref FindCalls);
            return Task.FromResult(seeded.FirstOrDefault(r => r.TenantId == tenantId && r.Role == role));
        }
    }

    private sealed class SetBasedRoles(IEnumerable<RoleDefinition> seeded) : IRoleDefinitionRepository
    {
        public int FindCalls;
        public int ListCalls;
        public Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default)
        {
            Interlocked.Increment(ref FindCalls);
            return Task.FromResult<RoleDefinition?>(null);
        }

        public Task<IReadOnlyDictionary<StaffRole, RoleDefinition>> ListByTenantAsync(string tenantId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ListCalls);
            return Task.FromResult<IReadOnlyDictionary<StaffRole, RoleDefinition>>(
                seeded.Where(r => r.TenantId == tenantId).ToDictionary(r => r.Role));
        }
    }

    private static RoleDefinition[] Seed(string tenant) =>
        Enum.GetValues<StaffRole>().Select(role => RoleDefinition.CreateSystem(tenant, role, new FixedClock())).ToArray();

    [Fact]
    public async Task DefaultImplementation_ReturnsEverySeededRole_ForTheRequestedTenantOnly()
    {
        var mine = Seed("tenant-a");
        var other = Seed("tenant-b");
        IRoleDefinitionRepository roles = new CountingRoles(mine.Concat(other));

        var catalogue = await roles.ListByTenantAsync("tenant-a");

        Assert.Equal(Enum.GetValues<StaffRole>().Length, catalogue.Count);
        Assert.All(catalogue.Values, r => Assert.Equal("tenant-a", r.TenantId));
    }

    [Fact]
    public async Task SetBasedRepository_IsQueriedOnce_AndNeverPerRole()
    {
        var roles = new SetBasedRoles(Seed("tenant-a"));

        var catalogue = await ((IRoleDefinitionRepository)roles).ListByTenantAsync("tenant-a");

        Assert.Equal(Enum.GetValues<StaffRole>().Length, catalogue.Count);
        Assert.Equal(1, roles.ListCalls);
        Assert.Equal(0, roles.FindCalls);
    }

    [Fact]
    public void ServerRepository_OverridesTheBatchLoad_AndStaffDirectoryUsesIt()
    {
        string root = Animora.Server.Tests.Licensing.LicensingSource.Read("src/Animora.Server/Authorization/ServerRoleDefinitionRepository.cs");
        Assert.Contains("ListByTenantAsync", root, StringComparison.Ordinal);

        string staff = Animora.Server.Tests.Licensing.LicensingSource.Read("src/Animora.Server/Auth/StaffProvisioningService.cs");
        Assert.Contains("roles.ListByTenantAsync(", staff, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (StaffRole role in Enum.GetValues<StaffRole>())", staff, StringComparison.Ordinal);
    }
}
