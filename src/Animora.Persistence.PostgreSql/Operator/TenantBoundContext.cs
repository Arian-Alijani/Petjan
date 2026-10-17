using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>
/// Builds a tenant-bound <see cref="AnimoraServerDbContext"/> over the operator context's connection so the vendor console
/// can read/write ONE clinic's tenant-filtered tables (users, products, ...) without ever disabling a tenant filter.
/// The caller owns the returned context and, for writes, enlists it in the operator transaction via
/// <c>UseTransactionAsync</c>. Pattern shared by <c>OwnerCredentialAdministration</c> / <c>ClinicStaffAdministration</c>.
/// </summary>
internal static class TenantBoundContext
{
    public static AnimoraServerDbContext Create(AnimoraOperatorDbContext db, string tenantId, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var binding = new AuthTenantContext();
        binding.Bind(tenantId);
        var options = new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(db.Database.GetDbConnection())
            .AddInterceptors(new TenantStampingInterceptor(binding, clock), new NoUnnamedIgnoreFiltersInterceptor(binding))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options;
        return new AnimoraServerDbContext(options, binding);
    }
}
