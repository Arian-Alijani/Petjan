using Animora.Persistence.PostgreSql.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Options;

public interface IDatabaseCapabilityProbe { Task<int> MaxConnectionsAsync(CancellationToken ct=default); }
public sealed class DatabaseCapabilityProbe([FromKeyedServices(PersistenceServiceCollectionExtensions.WorkerKey)] NpgsqlDataSource source) : IDatabaseCapabilityProbe
{
    public async Task<int> MaxConnectionsAsync(CancellationToken ct=default)
    { await using var cmd=source.CreateCommand("SHOW max_connections"); return int.Parse((string)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!,System.Globalization.CultureInfo.InvariantCulture); }
}
public sealed record ServerDatabaseStatistics(double SnapshotAge,long Pending,long Dead,long Intake,IReadOnlyDictionary<string,int> ActiveBySource,double OutboxLagSeconds=0,long ExpiredLeases=0);
public sealed class ServerDatabaseStatisticsReader([FromKeyedServices(PersistenceServiceCollectionExtensions.WorkerKey)] NpgsqlDataSource source)
{
    public async Task<ServerDatabaseStatistics> ReadAsync(CancellationToken ct=default)
    {
        await using var cmd=source.CreateCommand("""
            SELECT COALESCE((SELECT EXTRACT(EPOCH FROM now()-min(xact_start)) FROM pg_stat_activity
                WHERE application_name='Animora.Server/feed' AND xact_start IS NOT NULL),0)::double precision,
                (SELECT count(*) FROM public."ServerOutbox" WHERE "Status" IN (0,1)),
                (SELECT count(*) FROM public."ServerOutbox" WHERE "Status"=3),
                (SELECT count(*) FROM public."CommandIntake" WHERE "Status"=0),
                -- IMP-04: age of the oldest DUE outbox row (pending and past NextAttemptUtc, or a claim whose lease already expired).
                COALESCE((SELECT EXTRACT(EPOCH FROM now()-min(CASE WHEN "Status"=0 THEN "NextAttemptUtc" ELSE "ClaimedUntilUtc" END))
                    FROM public."ServerOutbox"
                    WHERE ("Status"=0 AND "NextAttemptUtc"<=now()) OR ("Status"=1 AND "ClaimedUntilUtc"<=now())),0)::double precision,
                -- IMP-04: intake-recovery backlog = InProgress rows whose lease expired (what IntakeRecoveryWorker drains).
                (SELECT count(*) FROM public."CommandIntake" WHERE "Status"=0 AND "LeaseUntilUtc"<now());
            SELECT application_name,count(*)::int FROM pg_stat_activity
                WHERE application_name LIKE 'Animora.Server/%' AND state<>'idle' GROUP BY application_name
            """);
        await using var reader=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false); await reader.ReadAsync(ct).ConfigureAwait(false);
        double age=reader.GetDouble(0); long pending=reader.GetInt64(1),dead=reader.GetInt64(2),intake=reader.GetInt64(3);
        double outboxLag=Math.Max(0,reader.GetDouble(4)); long expiredLeases=reader.GetInt64(5);
        await reader.NextResultAsync(ct).ConfigureAwait(false); var active=new Dictionary<string,int>();
        while(await reader.ReadAsync(ct).ConfigureAwait(false)) active[reader.GetString(0)]=reader.GetInt32(1);
        return new(age,pending,dead,intake,active,outboxLag,expiredLeases);
    }
}
