using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// B02 acceptance: enqueue and business write share one transaction (abort loses both, commit persists both);
/// <c>ClaimBatchAsync</c> under two concurrent callers returns disjoint sets; an expired lease is reclaimed by
/// the next pass; the sequence allocator is monotonic; a replay/mismatch follows the fingerprint rules.
/// </summary>
public sealed class CommandJournalTests
{
    private sealed record SimpleCommand(string Id, int Amount) : ICommand<int>, IJournaledCommand
    {
        public string CommandTypeName => "sales.invoice.post";
        public string AggregateTypeName => "Invoice";
        public string? AggregateIdOrNull => Id;
    }

    private sealed class SimpleHandler : ICommandHandler<SimpleCommand, int>
    {
        public Task<Result<int>> HandleAsync(SimpleCommand command, CommandContext context, CancellationToken ct) =>
            Task.FromResult(Result<int>.Ok(command.Amount));
    }

    private static CommandEnvelope Envelope(string? operationId = null, string fingerprint = "fp",
        long sequence = 1, string? dependsOn = null) => CommandEnvelope.Create(
        operationId ?? IdGenerator.NewId(), LocalStoreTestFixture.DefaultTenant, "U1", "D1",
        "sales.invoice.post", "Invoice", "INV-1", "{}", fingerprint, DateTimeOffset.UnixEpoch, sequence,
        dependsOnOperationId: dependsOn);

    [Fact]
    public async Task Enqueue_AndBusinessWrite_ShareOneTransaction_AbortLosesBoth()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();

            var outcome = await uow.ExecuteAsync(async ct =>
            {
                await journal.EnqueueAsync(Envelope(), ct);
                db.Add(Note("N-ABORT"));
                return Result<bool>.Fail(ErrorCodes.ValidationFailed, "refused"); // rollback
            });

            Assert.True(outcome.IsFailure);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(0, await db.Set<PendingCommand>().CountAsync());
            Assert.Equal(0, await db.Set<StoreMaintenanceLog>().CountAsync());
        }
    }

    [Fact]
    public async Task Enqueue_AndBusinessWrite_ShareOneTransaction_CommitPersistsBoth()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();

            var outcome = await uow.ExecuteAsync(async ct =>
            {
                await journal.EnqueueAsync(Envelope(), ct);
                db.Add(Note("N-COMMIT"));
                return Result<bool>.Ok(true);
            });
            Assert.True(outcome.IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(1, await db.Set<PendingCommand>().CountAsync());
            Assert.Equal(1, await db.Set<StoreMaintenanceLog>().CountAsync());
        }
    }

    [Fact]
    public async Task ConcurrentClaimers_GetDisjointSets()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture, count: 4);

        // Two independent contexts on the same file compete for the same rows.
        using var scopeA = fixture.CreateScope();
        using var scopeB = fixture.CreateScope();
        var journalA = scopeA.ServiceProvider.GetRequiredService<ICommandJournal>();
        var journalB = scopeB.ServiceProvider.GetRequiredService<ICommandJournal>();

        var a = await journalA.ClaimBatchAsync(2, TimeSpan.FromMinutes(5));
        var b = await journalB.ClaimBatchAsync(2, TimeSpan.FromMinutes(5));

        Assert.True(a.IsSuccess && b.IsSuccess);
        var idsA = a.Value!.Select(r => r.OperationId).ToHashSet();
        var idsB = b.Value!.Select(r => r.OperationId).ToHashSet();
        Assert.Equal(2, idsA.Count);
        Assert.Equal(2, idsB.Count);
        Assert.Empty(idsA.Intersect(idsB)); // never the same row twice
    }

    [Fact]
    public async Task ExpiredLease_IsReclaimed()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture, count: 1);

        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
            var first = await journal.ClaimBatchAsync(10, TimeSpan.FromSeconds(-1)); // already expired
            Assert.Single(first.Value!);
        }

        fixture.Clock.Now = fixture.Clock.UtcNow.AddMinutes(1); // advance past the lease

        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
            var second = await journal.ClaimBatchAsync(10, TimeSpan.FromMinutes(5));
            Assert.Single(second.Value!); // the crashed processor's work is reclaimable
        }
    }

    [Fact]
    public async Task SequenceAllocator_IsMonotonic()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var allocator = scope.ServiceProvider.GetRequiredService<ILocalSequenceAllocator>();
        var values = new System.Collections.Generic.List<long>();
        for (var i = 0; i < 5; i++) values.Add((await allocator.NextAsync()).Value);

        Assert.Equal(values.OrderBy(v => v), values);
        Assert.Equal(values.Distinct().Count(), values.Count);
    }

    [Fact]
    public async Task Enqueue_ReplayReturnsStored_MismatchIsIntegrityError()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var operationId = IdGenerator.NewId();

        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async ct =>
            {
                var first = await journal.EnqueueAsync(Envelope(operationId, "fp-A"), ct);
                Assert.True(first.IsSuccess);
                return Result<bool>.Ok(true);
            });
        }

        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Byte-identical replay: returns the stored record, adds no row.
            var replay = await uow.ExecuteAsync(async ct =>
            {
                var r = await journal.EnqueueAsync(Envelope(operationId, "fp-A"), ct);
                return Result<bool>.Ok(r.IsSuccess);
            });
            Assert.True(replay.Value);

            // Changed fingerprint: integrity error, never a replay.
            var mismatch = await uow.ExecuteAsync(ct => journal.EnqueueAsync(Envelope(operationId, "fp-B"), ct));
            Assert.True(mismatch.IsFailure);
            Assert.Equal(ErrorCodes.IdempotencyFingerprintMismatch, mismatch.Error);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(1, await db.Set<PendingCommand>().CountAsync());
        }
    }

    [Fact]
    public async Task ApplySettlement_RejectsAnIllegalTransition()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var envelope = Envelope();
        await SeedAsync(fixture, envelopes: [envelope], status: SettlementState.Settled);

        using var scope = fixture.CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
        var result = await journal.ApplySettlementAsync(new SettlementResult(envelope.OperationId,
            SettlementOutcome.Accepted, null, null, null, null, DateTimeOffset.UnixEpoch,
            FinancialEffect.Applied, null));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.SettlementInvalidTransition, result.Error);
    }

    [Fact]
    public async Task RejectedSettlement_FlagsTheLocalInvoice()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var invoice = Invoice.Create(fixture.TenantId, "INV-1", InvoiceKind.DirectSale, DateTimeOffset.UnixEpoch,
            "U1", ownerId: "OWN1", id: "INV-1").Value!;
        invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(12_500), 0m, 0m, InvoiceLineSource.Manual, null));
        Assert.True(invoice.Post(fixture.Clock).IsSuccess);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(invoice);
            await db.SaveChangesAsync();
        }
        var envelope = Envelope();
        await SeedAsync(fixture, envelopes: [envelope]);

        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
            var applied = await journal.ApplySettlementAsync(new SettlementResult(envelope.OperationId,
                SettlementOutcome.Rejected, ErrorCodes.StockWouldGoNegative.Code, null, null, null,
                DateTimeOffset.UnixEpoch, FinancialEffect.NotApplied, null));
            Assert.True(applied.IsSuccess, applied.Detail);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var loaded = await db.Set<Invoice>().AsNoTracking().SingleAsync();
            Assert.Equal(SettlementStatus.Rejected, loaded.Settlement);
            Assert.Equal(ErrorCodes.StockWouldGoNegative.Code, loaded.SettlementErrorCode);
            Assert.Equal(InvoiceState.Posted, loaded.State); // non-destructive: posted state is kept
        }
    }

    private static StoreMaintenanceLog Note(string detail) =>
        StoreMaintenanceLog.Create(StoreMaintenanceKind.Vacuum, detail, DateTimeOffset.UnixEpoch);

    private static async Task SeedAsync(LocalStoreTestFixture fixture, int count = 0,
        IReadOnlyList<CommandEnvelope>? envelopes = null,
        SettlementState status = SettlementState.LocallyPosted)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var list = envelopes ?? Enumerable.Range(1, count)
            .Select(i => Envelope(sequence: i)).ToArray();
        var sequence = 1L;
        foreach (var envelope in list)
        {
            var row = PendingCommand.Create(envelope, fixture.Clock.UtcNow);
            db.Add(row);
            _ = sequence++;
        }
        await db.SaveChangesAsync();

        if (status != SettlementState.LocallyPosted)
        {
            await db.Set<PendingCommand>().ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status));
        }
    }
}
