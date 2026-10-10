using System.Data;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Options;

/// <summary>Dedicated worker-pool, read-only transaction with database-enforced timeout and hard row cap.</summary>
public sealed class ReportingQuerySession : IAsyncDisposable
{
    public const int MaxRows=10000;
    public const int TimeoutSeconds=5;
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private Action? _release;
    private int _disposed;
    internal NpgsqlConnection Connection => _connection;
    internal NpgsqlTransaction Transaction => _transaction;
    internal void ReleaseOnDispose(Action release) => _release = release;
    private ReportingQuerySession(NpgsqlConnection connection,NpgsqlTransaction transaction) { _connection=connection; _transaction=transaction; }
    public static async Task<ReportingQuerySession> OpenAsync(NpgsqlDataSource source,CancellationToken ct)
    {
        var connection=await source.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            var tx=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct).ConfigureAwait(false);
            var session=new ReportingQuerySession(connection,tx);
            await using var setup=new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '5s'",connection,tx);
            await setup.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return session;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public async Task<IReadOnlyList<T>> ReadAsync<T>(string sql,Func<NpgsqlDataReader,T> map,IEnumerable<NpgsqlParameter>? parameters=null,CancellationToken ct=default)
    {
        await using var cmd=new NpgsqlCommand(sql,_connection,_transaction) { CommandTimeout=TimeoutSeconds };
        if(parameters is not null) foreach(var parameter in parameters) cmd.Parameters.Add(parameter);
        await using var reader=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false); var rows=new List<T>();
        while(await reader.ReadAsync(ct).ConfigureAwait(false))
        { if(rows.Count==MaxRows) throw new InvalidOperationException("Reporting row budget exceeded."); rows.Add(map(reader)); }
        return rows;
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await _transaction.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { await _connection.DisposeAsync().ConfigureAwait(false); }
            finally { Interlocked.Exchange(ref _release, null)?.Invoke(); }
        }
    }
}

public interface IReportingQuerySessionFactory { Task<ReportingQuerySession> OpenAsync(CancellationToken ct=default); }
public sealed class ReportingQuerySessionFactory(
    [Microsoft.Extensions.DependencyInjection.FromKeyedServices(Animora.Persistence.PostgreSql.DependencyInjection.PersistenceServiceCollectionExtensions.WorkerKey)] NpgsqlDataSource source) : IReportingQuerySessionFactory
{
    public Task<ReportingQuerySession> OpenAsync(CancellationToken ct=default)=>ReportingQuerySession.OpenAsync(source,ct);
}
