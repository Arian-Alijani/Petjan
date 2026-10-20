using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Hosting;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.Extensions.Options;
namespace Animora.Server.Admin;

/// <summary>
/// Operator-only full database backup. The pg_dump command line and the destination directory come from server
/// configuration (<c>Admin:PgDumpPath</c>, <c>Admin:BackupDirectory</c>) — never from the form — and every create/download is
/// recorded in the append-only operator audit (file name + size only, no connection details). When pg_dump is not
/// installed on the server the operator receives <c>STORE_UNAVAILABLE</c> (a clear Persian message), nothing is half-written.
/// </summary>
internal sealed class AdminBackupAdapter(AnimoraOperatorDbContext db,IOptions<DatabaseOptions> database,IOptions<AdminOptions> admin,
    AdminSession session,IHttpContextAccessor accessor,TimeProvider time,Animora.Domain.Time.IClock clock) : IAdminBackup
{
    private string Correlation=>accessor.HttpContext is { } http?CorrelationHeader.Resolve(http):Guid.NewGuid().ToString("N");

    public Task<Result<IReadOnlyList<BackupReceipt>>> ListAsync(CancellationToken ct=default)
    {
        _=session.OperatorId;
        IReadOnlyList<BackupReceipt> files=DatabaseBackupService.List(admin.Value.BackupDirectory).Select(f=>new BackupReceipt(f.FileName,f.SizeBytes,f.CreatedUtc)).ToArray();
        return Task.FromResult(Result<IReadOnlyList<BackupReceipt>>.Ok(files));
    }

    public async Task<Result<BackupReceipt>> CreateAsync(BackupRequestForm form,CancellationToken ct=default)
    {
        var actor=session.Actor;
        if(!form.Confirmed)return Result<BackupReceipt>.Fail(ErrorCodes.ValidationFailed);
        var result=await DatabaseBackupService.CreateAsync(database.Value.ConnectionString,admin.Value.BackupDirectory,admin.Value.PgDumpPath,time.GetUtcNow(),ct).ConfigureAwait(false);
        if(result.IsFailure)
        {
            await AuditAsync(actor,"backup.failed","failure",new{code=result.Error.Code},ct).ConfigureAwait(false);
            return Result<BackupReceipt>.Fail(result.Error);
        }
        var f=result.Value!;
        await AuditAsync(actor,"backup.created","success",new{file=f.FileName,bytes=f.SizeBytes},ct).ConfigureAwait(false);
        return Result<BackupReceipt>.Ok(new(f.FileName,f.SizeBytes,f.CreatedUtc));
    }

    /// <summary>Validates <paramref name="fileName"/> against the generated-name whitelist, audits the download and returns the on-disk path.</summary>
    public async Task<Result<string>> ResolveDownloadAsync(string? fileName,CancellationToken ct=default)
    {
        var actor=session.Actor;
        var path=DatabaseBackupService.Resolve(admin.Value.BackupDirectory,fileName);
        if(path is null)return Result<string>.Fail(ErrorCodes.NotFound);
        await AuditAsync(actor,"backup.downloaded","success",new{file=fileName},ct).ConfigureAwait(false);
        return Result<string>.Ok(path);
    }

    public Task<Result<IReadOnlyList<TenantBackupReceipt>>> ListTenantAsync(CancellationToken ct=default)
    {
        _=session.OperatorId;
        IReadOnlyList<TenantBackupReceipt> files=TenantBackupService.List(admin.Value.BackupDirectory)
            .Select(f=>new TenantBackupReceipt(f.FileName,f.TenantId,f.SizeBytes,f.CreatedUtc,f.Tables,f.Rows)).ToArray();
        return Task.FromResult(Result<IReadOnlyList<TenantBackupReceipt>>.Ok(files));
    }

    /// <summary>BAK-02: same guards as the whole-DB path — operator session, mandatory confirmation, config-derived directory, audit row.</summary>
    public async Task<Result<TenantBackupReceipt>> CreateTenantAsync(TenantBackupRequestForm form,CancellationToken ct=default)
    {
        var actor=session.Actor;
        if(!form.Confirmed)return Result<TenantBackupReceipt>.Fail(ErrorCodes.ValidationFailed);
        var tenantId=form.TenantId?.Trim()??string.Empty;
        var result=await TenantBackupService.CreateAsync(db,tenantId,clock,admin.Value.BackupDirectory,time.GetUtcNow(),ct).ConfigureAwait(false);
        if(result.IsFailure)
        {
            await AuditAsync(actor,"backup.tenant.failed","failure",new{code=result.Error.Code},ct,tenantId.Length is > 0 and <= 26?tenantId:null).ConfigureAwait(false);
            return Result<TenantBackupReceipt>.Fail(result.Error);
        }
        var f=result.Value!;
        await AuditAsync(actor,"backup.tenant.created","success",new{file=f.FileName,bytes=f.SizeBytes,tables=f.Tables,rows=f.Rows},ct,f.TenantId).ConfigureAwait(false);
        return Result<TenantBackupReceipt>.Ok(new(f.FileName,f.TenantId,f.SizeBytes,f.CreatedUtc,f.Tables,f.Rows));
    }

    /// <summary>Validates <paramref name="fileName"/> against the per-tenant name whitelist, audits the download and returns the on-disk path.</summary>
    public async Task<Result<string>> ResolveTenantDownloadAsync(string? fileName,CancellationToken ct=default)
    {
        var actor=session.Actor;
        var path=TenantBackupService.Resolve(admin.Value.BackupDirectory,fileName);
        if(path is null)return Result<string>.Fail(ErrorCodes.NotFound);
        await AuditAsync(actor,"backup.tenant.downloaded","success",new{file=fileName},ct,TenantBackupService.ParseTenantId(fileName)).ConfigureAwait(false);
        return Result<string>.Ok(path);
    }

    private async Task AuditAsync(Animora.Domain.Subscriptions.ActorRef actor,string action,string outcome,object detail,CancellationToken ct,string? tenantId=null)
    {
        db.Audit.Add(OperatorAudit.Create(time.GetUtcNow(),actor,action,tenantId,Correlation,outcome,detail));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
