using Animora.Application.Abstractions;
using Animora.Application.Behaviors;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// B02 acceptance: a journaled command enqueues exactly once inside the transaction; a replay of the same
/// OperationId + payload returns the stored record and adds no row; a changed payload is a fingerprint
/// mismatch (A-RULE 019) raised to the integrity sink exactly once, never a replay.
/// </summary>
public sealed class IdempotencyTests
{
    private sealed record JournaledCommand(string AggregateId, int Amount) : ICommand<string>, IJournaledCommand
    {
        public string CommandTypeName => "sales.invoice.post";
        public string AggregateTypeName => "Invoice";
        public string? AggregateIdOrNull => AggregateId;
    }

    private sealed class JournaledCommandHandler : ICommandHandler<JournaledCommand, string>
    {
        public Task<Result<string>> HandleAsync(JournaledCommand command, CommandContext context, CancellationToken ct) =>
            Task.FromResult(Result<string>.Ok("ok"));
    }

    private sealed record LocalCommand(string Note) : ICommand<string>; // not IJournaledCommand

    private sealed class LocalCommandHandler : ICommandHandler<LocalCommand, string>
    {
        public Task<Result<string>> HandleAsync(LocalCommand command, CommandContext context, CancellationToken ct) =>
            Task.FromResult(Result<string>.Ok("ok"));
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<Result> SaveAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());
        public Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default) =>
            work(ct);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
    }

    private static (ServiceProvider Sp, FakeCommandJournal Journal, RecordingIntegritySink Sink) Build()
    {
        var sink = new RecordingIntegritySink();
        var journal = new FakeCommandJournal(sink);
        var sequences = new CountingSequenceAllocator();
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork, NoopUnitOfWork>();
        services.AddSingleton<ICommandJournal>(journal);
        services.AddSingleton<ILocalSequenceAllocator>(sequences);
        services.AddSingleton<IIntegrityAlertSink>(sink);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IAuthorizationGate>(new RecordingAuthorizationGate(new StepLog(), Result.Ok()));
        services.AddSingleton<IEntitlementGate, AllowAllEntitlementGate>();
        services.AddAnimoraApplication(b =>
        {
            b.AddCommand<JournaledCommand, string, JournaledCommandHandler>();
            b.AddCommand<LocalCommand, string, LocalCommandHandler>();
        });
        return (services.BuildServiceProvider(), journal, sink);
    }

    private static CommandContext Ctx(string operationId) =>
        new("T1", "U1", "D1", operationId, "CORR", null, null, null, new FixedClock().UtcNow);

    [Fact]
    public async Task JournaledCommand_EnqueuesExactlyOnce_WithStableFingerprint()
    {
        var (sp, journal, _) = Build();
        var operationId = Domain.Primitives.IdGenerator.NewId();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        Assert.True((await dispatcher.SendAsync(new JournaledCommand("INV-1", 500), Ctx(operationId))).IsSuccess);
        Assert.Single(journal.Rows);
        var fingerprint = journal.Rows[0].CommandFingerprint;
        Assert.Equal(fingerprint, CommandFingerprint.Compute("sales.invoice.post",
            CommandFingerprint.CanonicalJson(new JournaledCommand("INV-1", 500))));
    }

    [Fact]
    public async Task Replay_SameOperationIdAndPayload_ReturnsStoredRecord_AddsNoRow()
    {
        var (sp, journal, sink) = Build();
        var operationId = Domain.Primitives.IdGenerator.NewId();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.SendAsync(new JournaledCommand("INV-1", 500), Ctx(operationId));
        await dispatcher.SendAsync(new JournaledCommand("INV-1", 500), Ctx(operationId));

        Assert.Single(journal.Rows); // no second row
        Assert.Equal(0, sink.Count); // a byte-identical retry is not an integrity event
    }

    [Fact]
    public async Task ChangedPayload_SameOperationId_IsFingerprintMismatch_AndRaisesSinkOnce()
    {
        var (sp, journal, sink) = Build();
        var operationId = Domain.Primitives.IdGenerator.NewId();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.SendAsync(new JournaledCommand("INV-1", 500), Ctx(operationId));
        var result = await dispatcher.SendAsync(new JournaledCommand("INV-1", 999), Ctx(operationId));

        Assert.Equal(ErrorCodes.IdempotencyFingerprintMismatch, result.Error);
        Assert.Single(journal.Rows);
        Assert.Equal(1, sink.Count);
        Assert.Equal(ErrorCodes.IdempotencyFingerprintMismatch.Code, sink.LastCode);
    }

    [Fact]
    public async Task NonJournaledCommand_NeverTouchesTheJournal()
    {
        var (sp, journal, _) = Build();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        Assert.True((await dispatcher.SendAsync(new LocalCommand("hi"), Ctx(Domain.Primitives.IdGenerator.NewId()))).IsSuccess);
        Assert.Empty(journal.Rows);
    }

    [Fact]
    public void Fingerprint_IsOrderInsensitive_AndChangesWithContent()
    {
        var a = CommandFingerprint.Compute("t", CommandFingerprint.CanonicalJson(new { b = 2, a = 1 }));
        var b = CommandFingerprint.Compute("t", CommandFingerprint.CanonicalJson(new { a = 1, b = 2 }));
        var c = CommandFingerprint.Compute("t", CommandFingerprint.CanonicalJson(new { a = 1, b = 3 }));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void Chain_PlacesCommandJournal_InsideTransaction()
    {
        var names = Dispatcher.Chain.Select(x => x.Name).ToArray();
        Assert.Equal(["Correlation", "ExceptionMapping", "Validation", "Authorization", "Entitlement",
            "Transaction", "CommandJournal", "Audit"], names); // B05a: Audit sits inside the transaction.
    }

    private sealed class CountingSequenceAllocator : ILocalSequenceAllocator
    {
        private long _value;
        public Task<Result<long>> NextAsync(CancellationToken ct = default) => Task.FromResult(Result<long>.Ok(++_value));
    }
}
