using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Animora.Application.Behaviors;
using Animora.Application.Ports;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Application.Tests;

public sealed class PostInvoiceCommandTests
{
    private sealed class FailureLogger : ILogger<ExceptionMappingBehavior>
    {
        public Exception? Failure { get; private set; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new EmptyScope();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Failure = exception;
        private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    }

    private sealed class SuccessfulAuditWriter : IAuditWriter
    {
        public Task<Result> WriteAsync(AuditRecord record, CancellationToken ct = default) => Task.FromResult(Result.Ok());
        public void Enlist(AuditRecord record) { }
    }

    private sealed class ImmediateUnitOfWork : IUnitOfWork
    {
        public Task<Result> SaveAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());
        public Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default) => work(ct);
    }

    [Fact]
    public async Task JournalReplay_ReusesStoredOutcome_WithoutSecondNumberOrMovement()
    {
        var invoice = SalesTestData.Draft();
        var numbers = new SalesNumberAllocator();
        var movements = new StockMovementRepositoryFake();
        var alerts = new RecordingIntegritySink();
        var journal = new FakeCommandJournal(alerts);
        var services = new ServiceCollection();
        services.AddSingleton<IInvoiceRepository>(new SalesInvoiceRepository(invoice));
        services.AddSingleton<IInvoiceNumberAllocator>(numbers);
        services.AddSingleton<IStockMovementRepository>(movements);
        services.AddSingleton<ISalesPostingData>(new SalesPostingFake());
        services.AddSingleton(SalesTestData.Clock);
        services.AddSingleton<IUnitOfWork, ImmediateUnitOfWork>();
        services.AddSingleton<ICommandJournal>(journal);
        services.AddSingleton<ILocalSequenceAllocator>(new CountingSequenceAllocator());
        services.AddSingleton<IIntegrityAlertSink>(alerts);
        services.AddSingleton<IAuditWriter, SuccessfulAuditWriter>();
        services.AddSingleton<IAuthorizationGate>(new RecordingAuthorizationGate(new StepLog(), Result.Ok()));
        services.AddSingleton<IEntitlementGate, AllowAllEntitlementGate>();
        var failureLogger = new FailureLogger();
        services.AddSingleton<ILogger<ExceptionMappingBehavior>>(failureLogger);
        services.AddAnimoraApplication(b => b
            .AddCommand<PostInvoiceCommand, Unit, PostInvoiceCommandHandler>()
            .AddValidator<PostInvoiceCommand, PostInvoiceCommandValidator>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var command = new PostInvoiceCommand(invoice.Id);

        var first = await dispatcher.SendAsync(command, SalesTestData.Context);
        var replay = await dispatcher.SendAsync(command, SalesTestData.Context);

        Assert.True(first.IsSuccess, failureLogger.Failure?.ToString() ?? first.Detail);
        Assert.True(replay.IsSuccess, replay.Detail);
        Assert.Single(journal.Rows);
        Assert.Single(movements.Added);
        Assert.Equal(1, numbers.Allocations);
    }

    [Fact]
    public async Task Posting_NumbersAndConsumesOnlyStockableLines()
    {
        var invoice = SalesTestData.Draft();
        var repo = new SalesInvoiceRepository(invoice);
        var movements = new StockMovementRepositoryFake();
        var numbers = new SalesNumberAllocator();
        var posting = new SalesPostingFake();
        var handler = new PostInvoiceCommandHandler(repo, numbers, movements, posting, SalesTestData.Clock);

        var result = await handler.HandleAsync(new PostInvoiceCommand(invoice.Id), SalesTestData.Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal("05-000001", invoice.InvoiceNumber);
        Assert.Equal(InvoiceState.Posted, invoice.State);
        Assert.Equal(SettlementStatus.LocallyPosted, invoice.Settlement);
        var movement = Assert.Single(movements.Added);
        Assert.Equal(MovementKind.Sale, movement.Kind);
        Assert.Equal(-2m, movement.Quantity.Value);
        Assert.Equal(invoice.Id, movement.SourceEntityId);
        Assert.Equal(SalesTestData.Context.OperationId, movement.OperationId);
        Assert.Equal(1, numbers.Allocations);
    }

    [Fact]
    public async Task PostingVisit_MarksHandoffInvoicedWithInvoiceId()
    {
        var invoice = SalesTestData.Draft();
        Assert.True(invoice.AttachVisit("VISIT-1").IsSuccess);
        var posting = new SalesPostingFake();
        var handler = new PostInvoiceCommandHandler(new SalesInvoiceRepository(invoice),
            new SalesNumberAllocator(), new StockMovementRepositoryFake(), posting, SalesTestData.Clock);

        var result = await handler.HandleAsync(new PostInvoiceCommand(invoice.Id), SalesTestData.Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(HandoffPostStatus.AlreadyInvoiced, posting.Status);
        Assert.Equal(invoice.Id, posting.InvoicedInvoiceId);
    }

    [Fact]
    public async Task AlreadyInvoicedHandoff_RefusesPostingBeforeNumberAndMovement()
    {
        var invoice = SalesTestData.Draft();
        Assert.True(invoice.AttachVisit("VISIT-1").IsSuccess);
        var numbers = new SalesNumberAllocator();
        var movements = new StockMovementRepositoryFake();
        var posting = new SalesPostingFake { Status = HandoffPostStatus.AlreadyInvoiced };
        var handler = new PostInvoiceCommandHandler(new SalesInvoiceRepository(invoice), numbers,
            movements, posting, SalesTestData.Clock);

        var result = await handler.HandleAsync(new PostInvoiceCommand(invoice.Id), SalesTestData.Context, default);

        Assert.Equal(ErrorCodes.HandoffAlreadyInvoiced, result.Error);
        Assert.Equal(InvoiceState.Draft, invoice.State);
        Assert.Equal(0, numbers.Allocations);
        Assert.Empty(movements.Added);
    }

    [Fact]
    public async Task NegativeStockIsRejectedWhenPolicyDisallowsIt()
    {
        var invoice = SalesTestData.Draft();
        var movements = new StockMovementRepositoryFake();
        var numbers = new SalesNumberAllocator();
        var handler = new PostInvoiceCommandHandler(new SalesInvoiceRepository(invoice), numbers,
            movements, new SalesPostingFake { AllowNegativeStock = false, OnHand = 0 }, SalesTestData.Clock);

        var result = await handler.HandleAsync(new PostInvoiceCommand(invoice.Id), SalesTestData.Context, default);

        Assert.Equal(ErrorCodes.StockWouldGoNegative, result.Error);
        Assert.Equal(0, numbers.Allocations);
        Assert.Empty(movements.Added);
    }
}

internal static class SalesTestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    public static readonly IClock Clock = new InventoryClock(Now);
    public static readonly CommandContext Context = new("T1", "U1", "D1",
        Animora.Domain.Primitives.IdGenerator.NewId(Now), "correlation-1", null, null, null, Now);

    public static Invoice Draft()
    {
        var invoice = Invoice.Create("T1", "draft-1", InvoiceKind.DirectSale, Now, "U1", ownerId: "OWN1").Value!;
        Assert.True(invoice.AddLine(new InvoiceLine("LINE1", InvoiceLineKind.Product, "CAT1", "PRODUCT1",
            "VARIANT1", "Medicine", new Quantity(2m, "PCS"), Money.Irt(1000), 0, 0,
            InvoiceLineSource.Catalog, "CAT1")).IsSuccess);
        Assert.True(invoice.AddLine(new InvoiceLine("LINE2", InvoiceLineKind.Service, "SVC1", null,
            null, "Examination", new Quantity(1m, "PCS"), Money.Irt(500), 0, 0,
            InvoiceLineSource.Catalog, "SVC1")).IsSuccess);
        return invoice;
    }

    public static Invoice Posted()
    {
        var invoice = Draft();
        Assert.True(invoice.Post(Clock).IsSuccess);
        return invoice;
    }
}

internal sealed class SalesInvoiceRepository(params Invoice[] initial) : IInvoiceRepository
{
    private readonly Dictionary<string, Invoice> _invoices = initial.ToDictionary(i => i.Id);
    public Task<Invoice?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(_invoices.GetValueOrDefault(id));
    public Task<Invoice> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(_invoices[id]);
    public void Add(Invoice entity) => _invoices[entity.Id] = entity;
    public void AddRange(IEnumerable<Invoice> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}

internal sealed class SalesPaymentRepository : IPaymentRepository
{
    public List<Payment> Added { get; } = [];
    public Task<Payment?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Added.FirstOrDefault(p => p.Id == id));
    public Task<Payment> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Added.First(p => p.Id == id));
    public void Add(Payment entity) => Added.Add(entity);
    public void AddRange(IEnumerable<Payment> entities) => Added.AddRange(entities);
}

internal sealed class SalesNumberAllocator : IInvoiceNumberAllocator
{
    public int Allocations { get; private set; }
    public Task<string> AllocateAsync(string tenantId, DateTimeOffset issuedUtc, CancellationToken ct = default) =>
        Task.FromResult($"05-{++Allocations:000000}");
}

internal sealed class SalesPostingFake : ISalesPostingData
{
    public bool AllowNegativeStock { get; set; } = true;
    public long OnHand { get; set; } = 10_000;
    public HandoffPostStatus Status { get; set; } = HandoffPostStatus.Ready;
    public string? InvoicedInvoiceId { get; private set; }
    public Task<long> GetOnHandScaledAsync(string productId, string variantId, CancellationToken ct) => Task.FromResult(OnHand);
    public Task<Money> GetLatestReceiptCostAsync(string productId, string variantId, CancellationToken ct) => Task.FromResult(Money.Irt(500));
    public Task<HandoffPostStatus> GetHandoffStatusAsync(string source, string sourceId, CancellationToken ct) => Task.FromResult(Status);
    public Task<HandoffPostStatus> MarkHandoffInvoicedAsync(string source, string sourceId, string invoiceId, CancellationToken ct)
    {
        var before = Status;
        if (before == HandoffPostStatus.Ready)
        {
            Status = HandoffPostStatus.AlreadyInvoiced;
            InvoicedInvoiceId = invoiceId;
        }
        return Task.FromResult(before);
    }
}
