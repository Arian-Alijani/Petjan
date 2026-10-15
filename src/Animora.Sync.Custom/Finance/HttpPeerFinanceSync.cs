using Animora.Application.Ports;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Sync.Custom.Finance;

/// <summary>
/// D04b — the client consumer of <c>GET /finance/ledger</c>. Pages are applied into the read-only
/// <see cref="PeerInvoice"/>/<see cref="PeerPayment"/> cache (upsert by stable id, never downgrading a newer row) and the
/// <see cref="PeerFinanceCursor"/> advances in the SAME save, so a crash mid-page re-reads the page instead of skipping it.
/// <para>
/// Nothing here touches the command pipeline, the replica store or the local business tables: money stays command-settled,
/// this is display data only. The caller (<c>SyncOrchestrator</c>) only invokes it behind an authenticated online session,
/// and this type is only registered when a server is configured — so an offline/unconfigured install never reaches it.
/// </para>
/// </summary>
public sealed class HttpPeerFinanceSync(SyncFeedClient client, IServiceScopeFactory scopes) : IPeerFinanceSync
{
    /// <summary>Rows requested per page (the server clamps to its own ceiling).</summary>
    internal const int PageSize = 200;

    /// <summary>Pages drained in one pass, so a first sync of a large tenant never monopolises a sync cycle.</summary>
    internal const int MaxPagesPerPass = 25;

    /// <inheritdoc />
    public async Task<Result<int>> PullAsync(CancellationToken ct = default)
    {
        var applied = 0;
        for (var pageNo = 0; pageNo < MaxPagesPerPass; pageNo++)
        {
            var cursor = await ReadCursorAsync(ct).ConfigureAwait(false);
            var fetched = await client.GetFinanceLedgerAsync(cursor, PageSize, ct).ConfigureAwait(false);
            if (fetched.IsFailure)
            {
                TraceLog.Warn($"PeerFinanceSync.Pull — ledger fetch failed ({fetched.Error}) detail='{fetched.Detail}' after {applied} row(s); cursor stays at {cursor}.", "sync.finance");
                return Result<int>.Fail(fetched.Error, fetched.Detail);
            }

            var page = fetched.Value!;
            applied += await ApplyAsync(page, ct).ConfigureAwait(false);
            // Stop on a drained window, or when the server could not move the cursor (defensive: never spin).
            if (!page.HasMore || page.NextCursorTicks <= cursor) break;
        }

        TraceLog.Info($"PeerFinanceSync.Pull — applied {applied} peer finance row(s).", "sync.finance");
        return Result<int>.Ok(applied);
    }

    private async Task<long> ReadCursorAsync(CancellationToken ct)
    {
        using var di = scopes.CreateScope();
        var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = di.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        return await db.Set<PeerFinanceCursor>().AsNoTracking()
            .Where(c => c.TenantId == tenant).Select(c => c.CursorTicks)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Upserts one page and advances the cursor atomically; returns the rows written.</summary>
    internal async Task<int> ApplyAsync(FinanceReadPage page, CancellationToken ct)
    {
        using var di = scopes.CreateScope();
        var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = di.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;
        var written = 0;

        var invoiceIds = page.Invoices.Select(i => i.Id).Distinct(StringComparer.Ordinal).ToList();
        var existingInvoices = await db.Set<PeerInvoice>().AsTracking()
            .Where(i => i.TenantId == tenant && invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        foreach (var row in page.Invoices)
        {
            if (string.IsNullOrWhiteSpace(row.Id)) continue;
            if (existingInvoices.TryGetValue(row.Id, out var current))
            {
                // A late/duplicate delivery of an older state must never roll the cache back.
                if (row.EffectiveTicks < current.EffectiveTicks) continue;
                current.Overwrite(row);
            }
            else
            {
                var created = PeerInvoice.From(tenant, row);
                db.Add(created);
                existingInvoices[row.Id] = created;
            }

            written++;
        }

        var paymentIds = page.Payments.Select(p => p.Id).Distinct(StringComparer.Ordinal).ToList();
        var existingPayments = await db.Set<PeerPayment>().AsTracking()
            .Where(p => p.TenantId == tenant && paymentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        foreach (var row in page.Payments)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.InvoiceId)) continue;
            if (existingPayments.TryGetValue(row.Id, out var current)) current.Overwrite(row);
            else
            {
                var created = PeerPayment.From(tenant, row);
                db.Add(created);
                existingPayments[row.Id] = created;
            }

            written++;
        }

        var cursor = await db.Set<PeerFinanceCursor>().AsTracking().SingleOrDefaultAsync(c => c.TenantId == tenant, ct).ConfigureAwait(false);
        if (cursor is null) db.Add(PeerFinanceCursor.Create(tenant, page.NextCursorTicks));
        else cursor.Advance(page.NextCursorTicks);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return written;
    }
}
