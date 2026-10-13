using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Operator;

/// <summary>Minimal operator-account facts every operator/admin request validates against.</summary>
internal sealed record OperatorAccountSnapshot(string SecurityStamp, OperatorAccountStatus Status, bool MustChangePassword)
{
    /// <summary>True when the account may act with a session carrying <paramref name="stamp"/>.</summary>
    public bool IsValidFor(string? stamp) => Status == OperatorAccountStatus.Active && string.Equals(SecurityStamp, stamp, StringComparison.Ordinal);
}

/// <summary>
/// BUG-OPLOCK-01: resolves the operator account at most ONCE per request. The cookie validator
/// (<c>OperatorCookie.OnValidatePrincipal</c>), the operator-API middleware and any later re-authentication all
/// go through this memo (stored in <see cref="HttpContext.Items"/>), so an admin/Blazor request costs at most
/// one operator-account read instead of one per authentication pass.
/// </summary>
internal static class OperatorRequestAccount
{
    private static readonly object ItemKey = new();

    private sealed record Entry(string? Id, Task<OperatorAccountSnapshot?> Snapshot);

    /// <summary>Reads (or returns the request-memoized) account snapshot; <c>null</c> when it does not exist.</summary>
    public static Task<OperatorAccountSnapshot?> GetAsync(HttpContext http, string? id)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.Items.TryGetValue(ItemKey, out var existing) && existing is Entry entry && string.Equals(entry.Id, id, StringComparison.Ordinal))
        {
            return entry.Snapshot;
        }

        Task<OperatorAccountSnapshot?> load = LoadAsync(http, id);
        http.Items[ItemKey] = new Entry(id, load);
        return load;
    }

    private static async Task<OperatorAccountSnapshot?> LoadAsync(HttpContext http, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var db = http.RequestServices.GetRequiredService<AnimoraOperatorDbContext>();
        return await db.Accounts.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new OperatorAccountSnapshot(x.SecurityStamp, x.Status, x.MustChangePassword))
            .SingleOrDefaultAsync(http.RequestAborted).ConfigureAwait(false);
    }
}
