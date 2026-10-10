using System;
using System.Threading;
using System.Threading.Tasks;
using Animora.Application.Abstractions;
using Animora.Domain.Events;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A10's atomic unit of work: one transaction covers work → save → event dispatch → handler writes →
/// commit; any failure rolls back; events dispatch exactly once in-transaction; transactions never
/// nest; and store failures are mapped to a <see cref="Result"/> rather than thrown.
/// </summary>
public sealed class UnitOfWorkTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A fake handler whose outcome and call count are controlled by the test.</summary>
    public sealed class RecordingHandler : IDomainEventHandler<AppointmentBookedEvent>
    {
        public static int Calls;
        public static bool ShouldFail;

        public static void Reset(bool shouldFail = false)
        {
            Calls = 0;
            ShouldFail = shouldFail;
        }

        public Task<Result> HandleAsync(AppointmentBookedEvent e, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(ShouldFail
                ? Result.Fail(ErrorCodes.ValidationFailed, "handler rejected")
                : Result.Ok());
        }
    }

    private static void RegisterHandler(IServiceCollection services) =>
        services.AddDomainEventHandler<AppointmentBookedEvent, RecordingHandler>();

    private static Appointment Booked(LocalStoreTestFixture fixture) =>
        Appointment.Book(fixture.TenantId, "PET1", "OWN1", "CHECKUP", Base, 30,
            AppointmentSource.Phone, "U1", fixture.Clock, doctorUserId: "DOC1").Value!;

    [Fact]
    public async Task SaveAsync_PersistsAndCommits()
    {
        RecordingHandler.Reset();
        using var fixture = new LocalStoreTestFixture(configureServices: RegisterHandler);
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            db.Add(Booked(fixture));

            Assert.True((await uow.SaveAsync()).IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(1, await db.Set<Appointment>().CountAsync());
        }
    }

    [Fact]
    public async Task FailingWork_RollsBackEverything()
    {
        RecordingHandler.Reset();
        using var fixture = new LocalStoreTestFixture(configureServices: RegisterHandler);
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var outcome = await uow.ExecuteAsync(async ct =>
            {
                db.Add(Booked(fixture));
                await Task.CompletedTask;
                return Result<bool>.Fail(ErrorCodes.ValidationFailed, "work refused");
            });

            Assert.True(outcome.IsFailure);
            Assert.Equal(ErrorCodes.ValidationFailed, outcome.Error);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(0, await db.Set<Appointment>().CountAsync());
        }
    }

    [Fact]
    public async Task Events_AreDispatchedExactlyOnceAndClearedAfterCommit()
    {
        RecordingHandler.Reset();
        using var fixture = new LocalStoreTestFixture(configureServices: RegisterHandler);
        await fixture.MigrateAsync();

        var appointment = Booked(fixture);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            db.Add(appointment);

            var outcome = await uow.ExecuteAsync(_ => Task.FromResult(Result<bool>.Ok(true)));

            Assert.True(outcome.IsSuccess);
        }

        Assert.Equal(1, RecordingHandler.Calls);
        Assert.Empty(appointment.DomainEvents);
    }

    [Fact]
    public async Task HandlerFailure_RollsBackAndKeepsTheEventOnTheAggregate()
    {
        RecordingHandler.Reset(shouldFail: true);
        using var fixture = new LocalStoreTestFixture(configureServices: RegisterHandler);
        await fixture.MigrateAsync();

        var appointment = Booked(fixture);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            db.Add(appointment);

            var outcome = await uow.ExecuteAsync(_ => Task.FromResult(Result<bool>.Ok(true)));

            Assert.True(outcome.IsFailure);
            Assert.Equal(ErrorCodes.ValidationFailed, outcome.Error);
        }

        Assert.Equal(1, RecordingHandler.Calls);
        Assert.NotEmpty(appointment.DomainEvents); // kept for a retry; not cleared on rollback.

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(0, await db.Set<Appointment>().CountAsync());
        }
    }

    [Fact]
    public async Task NestedTransactions_Throw()
    {
        RecordingHandler.Reset();
        using var fixture = new LocalStoreTestFixture(configureServices: RegisterHandler);
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync(async _ =>
        {
            await uow.ExecuteAsync(_ => Task.FromResult(Result<bool>.Ok(true)));
            return Result<bool>.Ok(true);
        }));
    }

    [Fact]
    public async Task StoreFailure_IsMappedToAResult()
    {
        RecordingHandler.Reset();
        using var fixture = new LocalStoreTestFixture(configureServices: RegisterHandler);
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Two entries with the same EntryNumber violate IX_JournalEntries_TenantId_EntryNumber:
        // a SQLITE_CONSTRAINT DbUpdateException that the unit of work maps to CONCURRENCY_CONFLICT (a data
        // conflict, deliberately not a store outage), never a crash.
        var outcome = await uow.ExecuteAsync(_ =>
        {
            db.Add(Entry("JE-DUP"));
            db.Add(Entry("JE-DUP"));
            return Task.FromResult(Result<bool>.Ok(true));
        });

        Assert.True(outcome.IsFailure);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, outcome.Error);
    }

    private static JournalEntry Entry(string number) =>
        JournalEntry.Create("T-LOCAL-DEFAULT", number, "1405-07", JournalSource.Manual,
            [
                new JournalLine("L1", AccountCodes.CashOnHand, EntrySide.Debit, Money.Irt(1_000), null, null),
                new JournalLine("L2", AccountCodes.ServiceRevenue, EntrySide.Credit, Money.Irt(1_000), null, null),
            ],
            "memo", "U1", Base).Value!;
}
