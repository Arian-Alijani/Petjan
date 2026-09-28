using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Domain.Events;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// C08 cheque lifecycle: each legal transition appends exactly one history entry, illegal ones are
/// refused, cleared cheques are no longer editable, clearing raises the ledger event, and a bulk
/// status change reports per-item partial success instead of swallowing failures.
/// </summary>
public sealed class ChequeLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly IClock Clock = new InventoryClock(Now);
    private static readonly CommandContext Context = new("T1", "U1", "D1",
        Animora.Domain.Primitives.IdGenerator.NewId(Now), "correlation-1", null, null, null, Now);

    private static Cheque Registered() =>
        Cheque.Register("T1", "CHQ-1", ChequeDirection.Inbound, Animora.Domain.ValueObjects.Money.Irt(1_000_000),
            Now.AddDays(7), Now, ownerId: "OWN1").Value!;

    [Fact]
    public async Task LegalTransition_MovesState_AndAppendsOneHistoryEntry()
    {
        var cheque = Registered();
        var repo = new ChequeRepositoryFake(cheque);
        var handler = new ChangeChequeStatusCommandHandler(repo, Clock);

        var result = await handler.HandleAsync(
            new ChangeChequeStatusCommand(cheque.Id, ChequeState.InPortfolio), Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(ChequeState.InPortfolio, cheque.State);
        Assert.Single(cheque.History);
    }

    [Fact]
    public async Task IllegalTransition_IsRefused()
    {
        var cheque = Registered();
        var handler = new ChangeChequeStatusCommandHandler(new ChequeRepositoryFake(cheque), Clock);

        // Registered → Cleared is not part of the machine.
        var result = await handler.HandleAsync(
            new ChangeChequeStatusCommand(cheque.Id, ChequeState.Cleared), Context, default);

        Assert.Equal(ErrorCodes.ChequeInvalidTransition, result.Error);
        Assert.Equal(ChequeState.Registered, cheque.State);
        Assert.Empty(cheque.History);
    }

    [Fact]
    public async Task Clearing_RaisesExactlyOneChequeClearedEvent()
    {
        var cheque = Registered();
        Assert.True(cheque.MoveToPortfolio("U1", Clock).IsSuccess);
        Assert.True(cheque.Deposit("U1", Clock).IsSuccess);
        cheque.ClearDomainEvents();
        var handler = new ChangeChequeStatusCommandHandler(new ChequeRepositoryFake(cheque), Clock);

        var result = await handler.HandleAsync(
            new ChangeChequeStatusCommand(cheque.Id, ChequeState.Cleared), Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(ChequeState.Cleared, cheque.State);
        Assert.Single(cheque.DomainEvents.OfType<ChequeClearedEvent>());
    }

    [Fact]
    public async Task EditingAClearedCheque_IsRefused()
    {
        var cheque = Registered();
        Assert.True(cheque.MoveToPortfolio("U1", Clock).IsSuccess);
        Assert.True(cheque.Deposit("U1", Clock).IsSuccess);
        Assert.True(cheque.MarkCleared("U1", Clock).IsSuccess);
        var handler = new UpdateChequeCommandHandler(new ChequeRepositoryFake(cheque));

        var result = await handler.HandleAsync(
            new UpdateChequeCommand(cheque.Id, "CHQ-1-NEW", Now.AddDays(30)), Context, default);

        Assert.Equal(ErrorCodes.ChequeNotEditable, result.Error);
    }

    [Fact]
    public async Task Bulk_ReportsPartialSuccess_ForOneIllegalItemAmongThree()
    {
        var ok1 = Registered();
        var ok2 = Registered();
        var illegal = Registered();
        // The illegal one is already terminal, so InPortfolio cannot apply to it.
        Assert.True(illegal.Cancel("U1", "voided", Clock).IsSuccess);
        var repo = new ChequeRepositoryFake(ok1, ok2, illegal);
        var handler = new BulkChangeChequeStatusCommandHandler(repo, Clock);

        var result = await handler.HandleAsync(
            new BulkChangeChequeStatusCommand([ok1.Id, ok2.Id, illegal.Id], ChequeState.InPortfolio), Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        var items = result.Value!;
        Assert.Equal(3, items.Count);
        Assert.Equal(2, items.Count(i => i.Succeeded));
        var failed = Assert.Single(items, i => !i.Succeeded);
        Assert.Equal(illegal.Id, failed.ChequeId);
        Assert.Equal(ErrorCodes.ChequeInvalidTransition.Code, failed.Error);
        Assert.Equal(ChequeState.InPortfolio, ok1.State);
        Assert.Equal(ChequeState.InPortfolio, ok2.State);
        // Each item carries a distinct operation id (A-RULE 006).
        Assert.Equal(3, items.Select(i => i.OperationId).Distinct().Count());
    }

    [Fact]
    public async Task BouncingWithoutReason_IsRefused()
    {
        var cheque = Registered();
        Assert.True(cheque.MoveToPortfolio("U1", Clock).IsSuccess);
        Assert.True(cheque.Deposit("U1", Clock).IsSuccess);
        var handler = new ChangeChequeStatusCommandHandler(new ChequeRepositoryFake(cheque), Clock);

        var result = await handler.HandleAsync(
            new ChangeChequeStatusCommand(cheque.Id, ChequeState.Bounced), Context, default);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
        Assert.Equal(ChequeState.Deposited, cheque.State);
    }
}

/// <summary>In-memory cheque store for the Application-layer tests.</summary>
internal sealed class ChequeRepositoryFake(params Cheque[] initial) : IChequeRepository
{
    private readonly Dictionary<string, Cheque> _cheques = initial.ToDictionary(c => c.Id);
    public Task<Cheque?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(_cheques.GetValueOrDefault(id));
    public Task<Cheque> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(_cheques[id]);
    public void Add(Cheque entity) => _cheques[entity.Id] = entity;
    public void AddRange(IEnumerable<Cheque> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}
