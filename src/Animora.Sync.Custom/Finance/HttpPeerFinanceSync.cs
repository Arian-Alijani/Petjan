using System.Text.Json;
using Animora.Application.Ports;
using Animora.Application.Activity;
using Animora.Contracts.History;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Activity;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
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

        // When new peer rows landed, re-fold the finance facts so the «آمار»/داشبورد read models reflect the
        // cross-device transactions too (the rebuilder dedups the peer twin of any local invoice). Done only on
        // change, and off the apply transaction; a rebuild failure is logged and never fails the display pull.
        if (applied > 0) await RebuildFinanceFactsAsync(ct).ConfigureAwait(false);

        TraceLog.Info($"PeerFinanceSync.Pull — applied {applied} peer finance row(s).", "sync.finance");
        return Result<int>.Ok(applied);
    }

    /// <summary>Recomputes the finance fact tables (now peer-aware) after a peer page changed the cache.</summary>
    private async Task RebuildFinanceFactsAsync(CancellationToken ct)
    {
        try
        {
            using var di = scopes.CreateScope();
            var maintenance = di.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();
            var rebuilt = await maintenance.RebuildFinanceProjectionsAsync(ct).ConfigureAwait(false);
            if (rebuilt.IsFailure)
                TraceLog.Warn($"PeerFinanceSync.Pull — finance fact rebuild returned {rebuilt.Error} ('{rebuilt.Detail}'); facts refresh on the next pass.", "sync.finance");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            TraceLog.Error("PeerFinanceSync.Pull — finance fact rebuild faulted; peer rows are cached, stats refresh next pass.", ex, "sync.finance");
        }
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
        var zone = di.ServiceProvider.GetService<IClinicTimeZoneProvider>() is { } zones
            ? await zones.GetClinicTimeZoneAsync(ct).ConfigureAwait(false) : TimeZoneInfo.Utc;
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

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

            if (row.PostedUtc is { } posted)
                await AppendAsync(ActivityEventKeys.InvoicePosted, "Invoice", row.Id, posted,
                    row.GrandTotalMinor, 1, HistoryEventStatus.Unpaid, new { row.OwnerId, Number = row.Number });
            if (row.VoidedUtc is { } voided)
                await AppendAsync(ActivityEventKeys.InvoiceVoided, "Invoice", row.Id, voided,
                    0, 0, HistoryEventStatus.Cancelled, new { Number = row.Number });
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

            await AppendAsync(ActivityEventKeys.PaymentRegistered, "Payment", row.Id, row.ReceivedUtc,
                Math.Abs(row.AmountMinor), Math.Sign(row.AmountMinor),
                row.AmountMinor < 0 ? HistoryEventStatus.Done : HistoryEventStatus.Paid,
                new { row.InvoiceId, row.Method });
            written++;
        }

        var cursor = await db.Set<PeerFinanceCursor>().AsTracking().SingleOrDefaultAsync(c => c.TenantId == tenant, ct).ConfigureAwait(false);
        if (cursor is null) db.Add(PeerFinanceCursor.Create(tenant, page.NextCursorTicks));
        else cursor.Advance(page.NextCursorTicks);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        // Peer finance is display-only and bypasses the business-table generation triggers. Invalidate the
        // dashboard atomically with the ledger page, including updates that append no new activity event.
        if (written > 0)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO DashboardGenerations (TenantId, Value) VALUES ({tenant}, 1)
                ON CONFLICT (TenantId) DO UPDATE SET Value = Value + 1
                """, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return written;

        Task<bool> AppendAsync(string title, string entity, string id, DateTimeOffset at,
            long amount, int sign, HistoryEventStatus status, object args) =>
            // This wire endpoint has no actor field. Unknown is honest; never stamp the fetching user.
            SyncedActivityFeed.AppendAsync(db, new ActivityEventDraft(IdGenerator.NewId(at), tenant, at,
                HistoryEventKind.Sale, title, JsonSerializer.Serialize(args), entity, id, amount, sign, status),
                zone, "finance", ct);
    }
}
