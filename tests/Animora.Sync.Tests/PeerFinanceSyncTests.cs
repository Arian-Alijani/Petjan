using Animora.Contracts.Wire.Sync;
using Animora.Application.Ports;
using Animora.Domain.ValueObjects;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Tests;
using Animora.Sync.Custom;
using Animora.Sync.Custom.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>D04b: the client consumer of <c>GET /finance/ledger</c> upserts a read-only peer cache and never touches the command pipeline.</summary>
public sealed class PeerFinanceSyncTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static FinanceInvoiceRow Inv(string id, long ticks, long due = 0, int state = (int)InvoiceState.Posted) =>
        new(id, "INV-" + id, (int)InvoiceKind.Visit, state, "O1", "Owner One", "Rex", T0, T0, 1000, 1000 - due, due, ticks);

    private static FinancePaymentRow Pay(string id, string invoiceId, long amount = 500) =>
        new(id, invoiceId, (int)PaymentMethod.Cash, amount, T0);

    private static async Task<LocalStoreTestFixture> NewFixtureAsync()
    {
        var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        return fixture;
    }

    private static HttpPeerFinanceSync Sync(LocalStoreTestFixture fixture, FakeFeedHandler handler) =>
        new(FeedFixtures.Client(handler), fixture.Services.GetRequiredService<IServiceScopeFactory>());

    private static async Task<(List<PeerInvoice> Invoices, List<PeerPayment> Payments, long Cursor)> ReadAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        return (await db.Set<PeerInvoice>().OrderBy(i => i.Id).ToListAsync(),
            await db.Set<PeerPayment>().OrderBy(p => p.Id).ToListAsync(),
            await db.Set<PeerFinanceCursor>().Select(c => c.CursorTicks).SingleOrDefaultAsync());
    }

    [Fact]
    public async Task Pull_UpsertsPeerRows_AndAdvancesTheCursor_ThenResumesFromIt()
    {
        using var fixture = await NewFixtureAsync();
        var seen = new List<string>();
        var handler = new FakeFeedHandler(request =>
        {
            seen.Add(request.RequestUri!.PathAndQuery);
            return seen.Count == 1
                ? FeedFixtures.JsonResponse(new FinanceReadPage([Inv("I1", 100, due: 400)], [Pay("P1", "I1")], 100, false))
                : FeedFixtures.JsonResponse(new FinanceReadPage([], [], 100, false));
        });
        var sync = Sync(fixture, handler);

        var first = await sync.PullAsync();
        Assert.Equal(2, first.Value);
        var state = await ReadAsync(fixture);
        Assert.Equal("I1", Assert.Single(state.Invoices).Id);
        Assert.Equal("INV-I1", state.Invoices[0].Number);
        Assert.Equal(400, state.Invoices[0].DueTotalMinor);
        Assert.Equal("I1", Assert.Single(state.Payments).InvoiceId);
        Assert.Equal(100, state.Cursor);

        var second = await sync.PullAsync();
        Assert.Equal(0, second.Value);
        Assert.StartsWith("/finance/ledger?since=0&", seen[0]);
        Assert.StartsWith("/finance/ledger?since=100&", seen[1]);
    }

    [Fact]
    public async Task Pull_NeverRollsACachedInvoiceBackToAnOlderDelivery()
    {
        using var fixture = await NewFixtureAsync();
        var pages = new Queue<FinanceReadPage>(
        [
            new([Inv("I1", 200, due: 0, state: (int)InvoiceState.Paid)], [], 200, false),
            new([Inv("I1", 100, due: 900)], [], 200, false), // stale re-delivery
        ]);
        var sync = Sync(fixture, new FakeFeedHandler(_ => FeedFixtures.JsonResponse(pages.Dequeue())));

        await sync.PullAsync();
        await sync.PullAsync();

        var state = await ReadAsync(fixture);
        Assert.Equal(InvoiceState.Paid, state.Invoices.Single().State);
        Assert.Equal(0, state.Invoices.Single().DueTotalMinor);
        Assert.Equal(200, state.Invoices.Single().EffectiveTicks);
    }

    [Fact]
    public async Task Pull_DrainsEveryPageWhileTheServerSaysThereIsMore()
    {
        using var fixture = await NewFixtureAsync();
        var pages = new Queue<FinanceReadPage>(
        [
            new([Inv("I1", 10)], [], 10, true),
            new([Inv("I2", 20)], [], 20, false),
        ]);
        var sync = Sync(fixture, new FakeFeedHandler(_ => FeedFixtures.JsonResponse(pages.Dequeue())));

        var result = await sync.PullAsync();

        Assert.Equal(2, result.Value);
        var state = await ReadAsync(fixture);
        Assert.Equal(["I1", "I2"], state.Invoices.Select(i => i.Id).ToArray());
        Assert.Equal(20, state.Cursor);
    }

    [Fact]
    public async Task Pull_ServerFailure_KeepsTheCursor_AndTouchesNothing()
    {
        using var fixture = await NewFixtureAsync();
        var sync = Sync(fixture, new FakeFeedHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)));

        var result = await sync.PullAsync();

        Assert.True(result.IsFailure);
        var state = await ReadAsync(fixture);
        Assert.Empty(state.Invoices);
        Assert.Empty(state.Payments);
        Assert.Equal(0, state.Cursor);
    }

    [Fact]
    public async Task Pull_DoesNotWriteAnyLocalBusinessTable()
    {
        using var fixture = await NewFixtureAsync();
        var sync = Sync(fixture, new FakeFeedHandler(_ =>
            FeedFixtures.JsonResponse(new FinanceReadPage([Inv("I1", 5)], [Pay("P1", "I1")], 5, false))));

        await sync.PullAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.Equal(0, await db.Set<Invoice>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.Set<Payment>().IgnoreQueryFilters().CountAsync());
    }
    [Fact]
    public async Task PeerReadModels_ShowServerInvoicesReceiptsCountsAndKpis_WithoutCreatingBusinessRows()
    {
        using var fixture = await NewFixtureAsync();
        var sync = Sync(fixture, new FakeFeedHandler(_ => FeedFixtures.JsonResponse(
            new FinanceReadPage([Inv("I1", 100, due: 400)], [Pay("P1", "I1", 600)], 100, false))));
        await sync.PullAsync();
        using var scope = fixture.CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesQueries>();
        var cashier = scope.ServiceProvider.GetRequiredService<ICashierQueries>();
        var list = await sales.GetInvoicesAsync("INV-I1", 1, 10, default);
        Assert.Equal("I1", Assert.Single(list.Items).Id);
        Assert.Equal("I1", Assert.Single(await sales.GetOwnerInvoicesAsync("O1", 10, default)).Id);
        var detail = await sales.GetInvoiceAsync("I1", default);
        Assert.NotNull(detail);
        Assert.True(detail.IsReadOnly);
        Assert.Empty(detail.Lines);
        Assert.Equal(600, Assert.Single(detail.Payments).AmountMinor);
        Assert.Equal(1, (await cashier.GetTabCountsAsync(default)).All);
        Assert.Single((await cashier.GetInvoicesAsync(CashierInvoiceTab.Open, null, 1, 10, default)).Items);
        var stats = await cashier.GetStatsAsync(T0.AddDays(-1), T0.AddDays(1), default);
        Assert.Equal(600, stats.CollectedMinor);
        Assert.Equal(1000, (await sales.GetKpisAsync(T0.AddDays(-1), T0.AddDays(1), default)).RevenueMinor);
        var history = await sales.GetHistoryAsync(null, null, null, null, "Newest", 1, 10, default);
        Assert.Equal(2, history.Page.Items.Count);
        Assert.Equal(600, history.InflowMinor);
    }

    [Fact]
    public async Task SettledOrigin_DeduplicatesById_WhileARejectedOriginPreservesItsProvisionalState()
    {
        using var fixture = await NewFixtureAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            foreach (var id in new[] { "I1", "I2" })
            {
                var invoice = Invoice.Create(fixture.TenantId, "LOCAL-" + id, InvoiceKind.DirectSale, T0,
                    "U1", ownerId: "O1", id: id).Value!;
                invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
                    new Quantity(1m, "PCS"), Money.Irt(1000), 0m, 0m, InvoiceLineSource.Manual, null));
                invoice.Post(fixture.Clock);
                invoice.MarkSettlement(id == "I1" ? SettlementStatus.Settled : SettlementStatus.Rejected, "REFUSED");
                db.Add(invoice);
            }
            await db.SaveChangesAsync();
        }
        await Sync(fixture, new FakeFeedHandler(_ => FeedFixtures.JsonResponse(
            new FinanceReadPage([Inv("I1", 100), Inv("I2", 100)], [], 100, false)))).PullAsync();
        using var read = fixture.CreateScope();
        var rows = (await read.ServiceProvider.GetRequiredService<ISalesQueries>().GetInvoicesAsync(null, 1, 10, default)).Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal("INV-I1", rows.Single(i => i.Id == "I1").Number);
        Assert.Equal("LOCAL-I2", rows.Single(i => i.Id == "I2").Number);
        Assert.Equal(SettlementStatus.Rejected, rows.Single(i => i.Id == "I2").SettlementStatus);
    }

    [Fact]
    public async Task PeerVoid_KeepsTheOriginalAcceptance_AndAppendsASeparateRecordedCancellation()
    {
        using var fixture = await NewFixtureAsync();
        var voided = T0.AddHours(2);
        var pages = new Queue<FinanceReadPage>([
            new([Inv("I1", T0.UtcTicks, due: 1000)], [], T0.UtcTicks, false),
            new([Inv("I1", voided.UtcTicks, state: (int)InvoiceState.Void) with { VoidedUtc = voided }], [], voided.UtcTicks, false),
            new([Inv("I1", voided.UtcTicks, state: (int)InvoiceState.Void) with { VoidedUtc = voided }], [], voided.UtcTicks, false),
        ]);
        var sync = Sync(fixture, new FakeFeedHandler(_ => FeedFixtures.JsonResponse(pages.Dequeue())));
        await sync.PullAsync();
        await sync.PullAsync();
        await sync.PullAsync();
        using var scope = fixture.CreateScope();
        var history = await scope.ServiceProvider.GetRequiredService<ISalesQueries>()
            .GetHistoryAsync(null, null, null, null, "Oldest", 1, 10, default);
        Assert.Equal(2, history.Page.Items.Count);
        var accepted = Assert.Single(history.Page.Items, i => i.Kind == "Accepted");
        Assert.Equal(T0, accepted.OccurredUtc);
        Assert.Equal(InvoiceState.Posted, accepted.InvoiceState);
        Assert.Equal(voided, Assert.Single(history.Page.Items, i => i.Kind == "Void").OccurredUtc);
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.Equal(2, await db.Set<ActivityEvent>().CountAsync(e => e.EntityId == "I1"));
        Assert.All(await db.Set<ActivityEvent>().ToListAsync(), e => Assert.Equal("", e.ActorUserId));
        Assert.Equal(0, history.InflowMinor);
    }

    [Fact]
    public async Task Pull_FoldsPeerInvoicesIntoTheFinanceFacts_SoStatisticsAreNotEmptyOnAPeer()
    {
        // Regression: a device that only *receives* another device's invoice (via GET /finance/ledger) must still
        // show it in the «آمار»/داشبورد facts, exactly as the Sales list already unions the peer cache. Before the fix
        // the fact tables were built from local Invoices only, so the stats/dashboard were empty on the peer.
        using var fixture = await NewFixtureAsync();
        var handler = new FakeFeedHandler(_ => FeedFixtures.JsonResponse(
            new FinanceReadPage([Inv("I1", T0.UtcTicks)], [], T0.UtcTicks, false)));

        var applied = await Sync(fixture, handler).PullAsync();
        Assert.Equal(1, applied.Value);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

        // Daily finance fact now reflects the peer invoice (count + grand-total revenue, 1000 minor units from Inv()).
        var daily = Assert.Single(await db.Set<DailyFinanceFact>().AsNoTracking().ToListAsync());
        Assert.Equal(1, daily.InvoiceCount);
        Assert.Equal(1000, daily.RevenueMinor);

        // Owner financial snapshot (the «برترین مشتریان» source) now attributes the spend to the peer invoice's owner.
        var owner = Assert.Single(await db.Set<OwnerFinancialSnapshot>().AsNoTracking().ToListAsync());
        Assert.Equal("O1", owner.OwnerId);
        Assert.Equal(1000, owner.TotalSpendMinor);
        Assert.Equal(1, owner.InvoiceCount);
    }

}
