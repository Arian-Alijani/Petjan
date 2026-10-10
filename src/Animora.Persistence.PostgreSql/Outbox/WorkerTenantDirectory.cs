using System.Runtime.CompilerServices;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Outbox;

/// <summary>Only enumerates tenant feed-state identifiers, on the worker pool; no business-data bypass.</summary>
public sealed class WorkerTenantDirectory(NpgsqlDataSource source)
{
    public async IAsyncEnumerable<string> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        string after = "";
        while (true)
        {
            string? next;
            await using (var c = await source.OpenConnectionAsync(ct).ConfigureAwait(false))
            {
                await using var cmd = new NpgsqlCommand("SELECT \"TenantId\" FROM public.\"TenantFeedState\" WHERE \"TenantId\">@after ORDER BY \"TenantId\" LIMIT 1", c);
                cmd.Parameters.AddWithValue("after", after);
                next = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            }
            if (next is null) yield break;
            after = next; yield return next; // Connection released before consumer work, even with WorkerMax=1.
        }
    }
}
