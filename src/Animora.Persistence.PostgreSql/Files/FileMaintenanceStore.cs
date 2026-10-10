using Microsoft.Extensions.DependencyInjection;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Files;

/// <summary>Explicit cross-tenant file maintenance, confined to the dedicated worker pool.</summary>
public sealed class FileMaintenanceStore([FromKeyedServices(PersistenceServiceCollectionExtensions.WorkerKey)] NpgsqlDataSource source)
{
    private string _tenantCursor = "", _idCursor = "";
    public async Task<IReadOnlyList<(string TenantId, string Id)>> CandidatesAsync(bool sample, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand(sample
            ? "SELECT \"TenantId\",\"Id\" FROM public.\"FileObjects\" WHERE \"State\"=4 ORDER BY random() LIMIT 20"
            : "SELECT \"TenantId\",\"Id\" FROM public.\"FileObjects\" WHERE \"State\" IN (0,1,3,4,5) AND (\"TenantId\",\"Id\")>(@tenant,@id) ORDER BY \"TenantId\",\"Id\" LIMIT 100");
        if (!sample) { cmd.Parameters.AddWithValue("tenant", _tenantCursor); cmd.Parameters.AddWithValue("id", _idCursor); }
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<(string, string)>(); while (await reader.ReadAsync(ct).ConfigureAwait(false)) rows.Add((reader.GetString(0), reader.GetString(1)));
        if (!sample) { if (rows.Count == 0) { _tenantCursor = ""; _idCursor = ""; } else { (_tenantCursor, _idCursor) = rows[^1]; } }
        return rows;
    }
    public async Task<IReadOnlyList<string>> GarbageAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("SELECT \"Sha256\" FROM public.\"BlobRefs\" WHERE \"RefCount\"=0 AND COALESCE(\"UnreferencedUtc\",\"FirstSeenUtc\")<@before LIMIT 20");
        cmd.Parameters.AddWithValue("before", before); var hashes = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) hashes.Add(reader.GetString(0).Trim()); return hashes;
    }
    public async Task<bool> CollectAsync(string hash, DateTimeOffset before, Func<Task> delete, bool orphan, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var fence = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", connection, tx);
        fence.Parameters.AddWithValue("key", "file-blob:" + hash); await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await using var check = new NpgsqlCommand("SELECT \"RefCount\",COALESCE(\"UnreferencedUtc\",\"FirstSeenUtc\") FROM public.\"BlobRefs\" WHERE \"Sha256\"=@hash FOR UPDATE", connection, tx);
        check.Parameters.AddWithValue("hash", hash);
        bool allowed;
        await using (var reader = await check.ExecuteReaderAsync(ct).ConfigureAwait(false))
        { bool exists = await reader.ReadAsync(ct).ConfigureAwait(false); allowed = orphan ? !exists : exists && reader.GetInt32(0) == 0 && reader.GetFieldValue<DateTimeOffset>(1) < before; }
        if (!allowed) return false;
        // Locked row is the durable GC mark: after a crash it remains zero-ref and is retried.
        await delete().ConfigureAwait(false);
        if (!orphan)
        {
            await using var remove = new NpgsqlCommand("DELETE FROM public.\"BlobRefs\" WHERE \"Sha256\"=@hash AND \"RefCount\"=0", connection, tx);
            remove.Parameters.AddWithValue("hash", hash); await remove.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await AuditAsync(connection,tx,orphan?"orphan":"zero_ref",null,ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false); return true;
    }
    public async Task<bool> CollectPublisherTemporaryAsync(string hash, Func<bool> delete, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var fence = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", connection, tx);
        fence.Parameters.AddWithValue("key", "file-blob:" + hash);
        await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        // A publisher temp is never the committed blob. Existing references must remain untouched.
        if (!delete()) return false;
        await AuditAsync(connection, tx, "orphan", null, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }
    public async Task<bool> CollectStagingAsync(string tenantId,string uploadId,Func<Task> delete,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var fence=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",connection,tx);
        fence.Parameters.AddWithValue("key","file-upload:"+tenantId+":"+uploadId);await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await using var check=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM public.\"FileObjects\" WHERE \"TenantId\"=@tenant AND \"UploadId\"=@upload AND \"State\" IN (0,1,2,6))",connection,tx);
        check.Parameters.AddWithValue("tenant",tenantId);check.Parameters.AddWithValue("upload",uploadId);
        if((bool)(await check.ExecuteScalarAsync(ct).ConfigureAwait(false))!)return false;
        await delete().ConfigureAwait(false);await AuditAsync(connection,tx,"orphan",tenantId,ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);return true;
    }
    private static async Task AuditAsync(NpgsqlConnection connection,NpgsqlTransaction tx,string finding,string? tenantId,CancellationToken ct)
    {
        var now=DateTimeOffset.UtcNow;var id=Animora.Domain.Primitives.IdGenerator.NewId(now);
        await using var audit=new NpgsqlCommand("""
            INSERT INTO public."OperatorAudit" ("Id","OccurredUtc","OperatorId","ActorKind","Action","TargetTenantId","Outcome","CorrelationId","DetailJson")
            VALUES (@id,@now,NULL,@kind,@action,@tenant,'success',@id,'{}'::jsonb)
            """,connection,tx);
        audit.Parameters.AddWithValue("id",id);audit.Parameters.AddWithValue("now",now);
        audit.Parameters.AddWithValue("kind",(short)Animora.Domain.Subscriptions.ActorKind.System);
        audit.Parameters.AddWithValue("action","files.reconcile."+finding);
        audit.Parameters.Add(new NpgsqlParameter("tenant",NpgsqlTypes.NpgsqlDbType.Varchar) {Value=(object?)tenantId??DBNull.Value});
        await audit.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
    public async Task WriteManifestAsync(Func<string, Task> write, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("SELECT \"Sha256\",\"SizeBytes\" FROM public.\"BlobRefs\" WHERE \"RefCount\">0 ORDER BY \"Sha256\"");
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            await write(reader.GetString(0).Trim() + " " + reader.GetInt64(1).ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
    }
}
