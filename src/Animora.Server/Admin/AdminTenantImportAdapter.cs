using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Server.Hosting;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Admin;

/// <summary>
/// Operator-only adapter for validating and ingesting a legacy <c>Clinic.bak</c> import bundle (<c>clinic-import.animora.json</c>,
/// format v2) into a single clinic's tenant (T8). Runs on a dedicated <see cref="TenantBoundContext"/> enlisted in the operator
/// transaction and writes both operator and tenant security audit records.
/// </summary>
internal sealed class AdminTenantImportAdapter(
    AnimoraOperatorDbContext db,
    AdminSession session,
    IHttpContextAccessor accessor,
    TimeProvider time,
    IClock clock) : IAdminTenantImport
{
    private string Correlation =>
        accessor.HttpContext is { } http ? CorrelationHeader.Resolve(http) : Guid.NewGuid().ToString("N");

    public async Task<Result<TenantImportReceipt>> ValidateAsync(
        string tenantId,
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken ct = default)
    {
        _ = session.OperatorId;
        var cleanTenant = tenantId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanTenant) || cleanTenant.Length > 26)
        {
            return Result<TenantImportReceipt>.Fail(ErrorCodes.ValidationFailed);
        }

        if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == cleanTenant && !t.Deleted, ct).ConfigureAwait(false))
        {
            return Result<TenantImportReceipt>.Fail(ErrorCodes.NotFound);
        }

        var parsed = ClinicBundleImportService.ParseAndValidate(utf8Json.Span, out var summary, cleanTenant);
        if (parsed.IsFailure || summary is null)
        {
            return Result<TenantImportReceipt>.Fail(parsed.Error, parsed.Detail);
        }

        return Result<TenantImportReceipt>.Ok(Map(summary));
    }

    public async Task<Result<TenantImportReceipt>> ImportAsync(
        string tenantId,
        ReadOnlyMemory<byte> utf8Json,
        bool confirmed,
        CancellationToken ct = default)
    {
        var actor = session.Actor;
        var cleanTenant = tenantId?.Trim() ?? string.Empty;
        if (!confirmed || string.IsNullOrWhiteSpace(cleanTenant) || cleanTenant.Length > 26)
        {
            return Result<TenantImportReceipt>.Fail(ErrorCodes.ValidationFailed);
        }

        var result = await ClinicBundleImportService.ImportAsync(
            db,
            cleanTenant,
            utf8Json,
            actor,
            Correlation,
            clock,
            ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            db.Audit.Add(OperatorAudit.Create(
                time.GetUtcNow(),
                actor,
                ClinicBundleImportService.ImportFailedAuditAction,
                cleanTenant,
                Correlation,
                "failure",
                new { code = result.Error.Code }));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<TenantImportReceipt>.Fail(result.Error, result.Detail);
        }

        return Result<TenantImportReceipt>.Ok(Map(result.Value!));
    }

    private static TenantImportReceipt Map(ClinicImportSummary s) =>
        new(
            s.TenantId,
            s.FormatVersion,
            s.SourceFingerprint,
            s.Owners,
            s.Pets,
            s.Visits,
            s.Vaccinations,
            s.Antiparasites,
            s.LabResults,
            s.Appointments,
            s.Invoices,
            s.StockReceipts,
            s.Products,
            s.Services,
            s.Categories,
            s.Insurers,
            s.Warnings);
}
