using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Cashier;
using Animora.Domain.Events;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// C08 cash-session lifecycle: open uniqueness, cash-payment session enforcement, close variance
/// and the note rule. Every handler is exercised through its real domain aggregate — no logic is
/// duplicated in the tests.
/// </summary>
public sealed class CashSessionUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly IClock Clock = new InventoryClock(Now);
    private static readonly CommandContext Context = new("T1", "U1", "D1",
        Animora.Domain.Primitives.IdGenerator.NewId(Now), "correlation-1", null, null, null, Now);

    [Fact]
    public async Task Opening_ASecondSession_IsRefused()
    {
        var repo = new CashSessionRepositoryFake();
        var context = new CashSessionContextFake(repo);
        var handler = new OpenCashSessionCommandHandler(context, repo, new CountingSequenceAllocator(), Clock);

        var first = await handler.HandleAsync(new OpenCashSessionCommand(Money.Irt(100_000)), Context, default);
        var second = await handler.HandleAsync(new OpenCashSessionCommand(Money.Irt(100_000)), Context, default);

        Assert.True(first.IsSuccess, first.Detail);
        Assert.Equal(ErrorCodes.CashSessionAlreadyOpen, second.Error);
        Assert.Single(repo.Sessions);
    }

    [Fact]
    public async Task CashPayment_WithoutOpenSession_IsRefused_WhileCardSucceeds()
    {
        var invoice = SalesTestData.Posted();
        var payments = new SalesPaymentRepository();
        var context = new CashSessionContextFake(new CashSessionRepositoryFake());
        var handler = new RegisterPaymentCommandHandler(
            new SalesInvoiceRepository(invoice), payments, Clock, context);

        var cash = await handler.HandleAsync(
            new RegisterPaymentCommand(invoice.Id, PaymentMethod.Cash, Money.Irt(500)), Context, default);
        var card = await handler.HandleAsync(
            new RegisterPaymentCommand(invoice.Id, PaymentMethod.Card, Money.Irt(500), Reference: "CARD-1"), Context, default);

        Assert.Equal(ErrorCodes.CashSessionRequired, cash.Error);
        Assert.True(card.IsSuccess, card.Detail);
        Assert.Single(payments.Added);
    }

    [Fact]
    public async Task CashPayment_WithOpenSession_StampsSessionAndRecordsMovement()
    {
        var invoice = SalesTestData.Posted();
        var payments = new SalesPaymentRepository();
        var repo = new CashSessionRepositoryFake();
        var context = new CashSessionContextFake(repo);
        var open = new OpenCashSessionCommandHandler(context, repo, new CountingSequenceAllocator(), Clock);
        Assert.True((await open.HandleAsync(new OpenCashSessionCommand(Money.Irt(100_000)), Context, default)).IsSuccess);
        var drawer = repo.Sessions.Single();
        var handler = new RegisterPaymentCommandHandler(
            new SalesInvoiceRepository(invoice), payments, Clock, context);

        var result = await handler.HandleAsync(
            new RegisterPaymentCommand(invoice.Id, PaymentMethod.Cash, Money.Irt(500)), Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        var payment = Assert.Single(payments.Added);
        Assert.Equal(drawer.Id, payment.CashSessionId);
        var movement = Assert.Single(drawer.Movements);
        Assert.Equal(CashMovementKind.SalePayment, movement.Kind);
        Assert.Equal(500, movement.Amount.Minor);
    }

    [Fact]
    public async Task Closing_RecordsExactVariance()
    {
        var repo = new CashSessionRepositoryFake();
        var context = new CashSessionContextFake(repo);
        var open = new OpenCashSessionCommandHandler(context, repo, new CountingSequenceAllocator(), Clock);
        Assert.True((await open.HandleAsync(new OpenCashSessionCommand(Money.Irt(100_000)), Context, default)).IsSuccess);
        var drawer = repo.Sessions.Single();
        var close = new CloseCashSessionCommandHandler(context, new CashierQueriesFake(), Clock);

        // Declared 100,003 against an expected 100,000 float → a +3 variance, explained by a note.
        var result = await close.HandleAsync(new CloseCashSessionCommand(Money.Irt(100_003), "extra change"), Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(3, drawer.Variance.Minor);
        Assert.Equal(CashSessionState.Closed, drawer.State);
        Assert.Contains(drawer.DomainEvents, e => e is CashSessionClosedEvent);
    }

    [Fact]
    public async Task Closing_WithVarianceButNoNote_RequiresNote()
    {
        var repo = new CashSessionRepositoryFake();
        var context = new CashSessionContextFake(repo);
        var open = new OpenCashSessionCommandHandler(context, repo, new CountingSequenceAllocator(), Clock);
        Assert.True((await open.HandleAsync(new OpenCashSessionCommand(Money.Irt(100_000)), Context, default)).IsSuccess);
        var close = new CloseCashSessionCommandHandler(context, new CashierQueriesFake(), Clock);

        var result = await close.HandleAsync(new CloseCashSessionCommand(Money.Irt(90_000), null), Context, default);

        Assert.Equal(ErrorCodes.CashVarianceNoteRequired, result.Error);
        Assert.Equal(CashSessionState.Open, repo.Sessions.Single().State);
    }

    [Fact]
    public async Task RecordMovement_AppendsToOpenSession()
    {
        var repo = new CashSessionRepositoryFake();
        var context = new CashSessionContextFake(repo);
        var open = new OpenCashSessionCommandHandler(context, repo, new CountingSequenceAllocator(), Clock);
        Assert.True((await open.HandleAsync(new OpenCashSessionCommand(Money.Irt(100_000)), Context, default)).IsSuccess);
        var drawer = repo.Sessions.Single();
        var handler = new RecordCashMovementCommandHandler(context, Clock);

        var result = await handler.HandleAsync(
            new RecordCashMovementCommand(CashMovementKind.FloatIn, Money.Irt(20_000), "top up"), Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Single(drawer.Movements);
        Assert.Equal(120_000, drawer.ExpectedClosing.Minor);
    }
}

/// <summary>In-memory cash-session store for the Application-layer tests.</summary>
internal sealed class CashSessionRepositoryFake : ICashSessionRepository
{
    public List<CashSession> Sessions { get; } = [];
    public Task<CashSession?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Sessions.FirstOrDefault(s => s.Id == id));
    public Task<CashSession> RequireAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Sessions.First(s => s.Id == id));
    public void Add(CashSession entity) => Sessions.Add(entity);
    public void AddRange(IEnumerable<CashSession> entities) => Sessions.AddRange(entities);
}

/// <summary>Resolves the newest still-open drawer from the fake repository.</summary>
internal sealed class CashSessionContextFake(CashSessionRepositoryFake repo) : ICashSessionContext
{
    public Task<CashSession?> GetOpenSessionAsync(string tenantId, string userId, CancellationToken ct) =>
        Task.FromResult(repo.Sessions.LastOrDefault(s =>
            s.TenantId == tenantId && s.CashierUserId == userId && s.State == CashSessionState.Open));
}

/// <summary>A cashier-queries stub; close tests never have unsettled payments.</summary>
internal sealed class CashierQueriesFake : ICashierQueries
{
    public bool Unsettled { get; init; }
    public Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(CashierInvoiceTab tab, string? search, int page, int pageSize, CancellationToken ct) =>
        Task.FromResult(new PagedResult<InvoiceSummaryReadModel>([], 0, page, pageSize));
    public Task<CashierStatsReadModel> GetStatsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
        Task.FromResult(new CashierStatsReadModel(0, 0, 0, 0, 0));
    public Task<CashierTabCountsReadModel> GetTabCountsAsync(CancellationToken ct) =>
        Task.FromResult(new CashierTabCountsReadModel(0, 0, 0, 0));
    public Task<OpenCashSessionReadModel?> GetOpenSessionAsync(string userId, CancellationToken ct) =>
        Task.FromResult<OpenCashSessionReadModel?>(null);
    public Task<bool> HasUnsettledPaymentsAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(Unsettled);
}
