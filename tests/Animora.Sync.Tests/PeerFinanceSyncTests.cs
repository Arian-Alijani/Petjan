using Animora.Contracts.Wire.Sync;
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
            new([Inv("I1", 200, due: 0, state: (int)InvoiceState.Settled)], [], 200, false),
            new([Inv("I1", 100, due: 900)], [], 200, false), // stale re-delivery
        ]);
        var sync = Sync(fixture, new FakeFeedHandler(_ => FeedFixtures.JsonResponse(pages.Dequeue())));

        await sync.PullAsync();
        await sync.PullAsync();

        var state = await ReadAsync(fixture);
        Assert.Equal(InvoiceState.Settled, state.Invoices.Single().State);
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
}
