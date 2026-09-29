using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Finance;
using Animora.Application.Ports;
using Animora.Application.UseCases.Finance;
using Animora.Domain.Events;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Application.Tests;

public sealed class PeriodCloseTests
{
    private const string Tenant = "T-LOCAL-DEFAULT";
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Close_RecordsReferenceAndNeverAllowsReopen()
    {
        var period = NewPeriod("1405-06", Start, Start.AddMonths(1));
        var repo = new Periods(period);
        var context = new CommandContext(Tenant, "U1", "D1", "OP1", "COR1", null, null, null, Start);
        var handler = new CloseAccountingPeriodCommandHandler(repo, new FixedClock());
        var closed = await handler.HandleAsync(new CloseAccountingPeriodCommand(period.PeriodCode), context, default);
        Assert.True(closed.IsSuccess, closed.Detail);
        Assert.Equal(PeriodState.Closed, period.State);
        Assert.False(string.IsNullOrWhiteSpace(period.ClosingEntryId));
        Assert.Equal("U1", period.ClosedByUserId);
        Assert.Equal(ErrorCodes.PeriodClosed, AccountingPeriod.EnsurePostable(period).Error);
        Assert.False(AccountingPeriod.IsTransitionAllowed(PeriodState.Closed, PeriodState.Open));
        Assert.Equal(ErrorCodes.PeriodInvalidTransition, period.BeginClose().Error);
        var missing = await handler.HandleAsync(new CloseAccountingPeriodCommand("missing"), context, default);
        Assert.Equal(ErrorCodes.PeriodNotFound, missing.Error);
    }

    [Fact]
    public async Task LateEvent_FromClosedPeriod_PostsInNextOpenPeriod_WithOriginalEffectiveTime()
    {
        var closed = NewPeriod("1405-06", Start, Start.AddMonths(1));
        closed.BeginClose();
        closed.Close(false, false, false, new FixedClock(), "CLOSE-REF", "U1");
        var next = NewPeriod("1405-07", closed.EndUtc, closed.EndUtc.AddMonths(1));
        var periods = new Periods(closed, next);
        var ledger = new Entries();
        var handler = new LedgerPostingHandler(periods, new Numbers(), ledger,
            null!, null!, null!, null!, new CorrelationContext(), new FixedClock());
        var instant = Start.AddDays(2);
        var e = new ChequeClearedEvent("EV1", Tenant, instant, "CH1", ChequeDirection.Inbound, 2500, "IRT");
        var posted = await handler.HandleAsync(e, default);
        Assert.True(posted.IsSuccess, posted.Detail);
        Assert.Equal(closed.EndUtc, periods.NextOpenAfter);
        var entry = Assert.Single(ledger.Items);
        Assert.Equal(next.PeriodCode, entry.PeriodId);
        Assert.Equal(instant, entry.EffectiveUtc);
        Assert.Equal(new FixedClock().UtcNow, entry.PostedUtc);
    }

    private static AccountingPeriod NewPeriod(string code, DateTimeOffset from, DateTimeOffset to) =>
        AccountingPeriod.Create(Tenant, code, from, to).Value!;

    private sealed class Periods(params AccountingPeriod[] items) : IPeriodRepository
    {
        public DateTimeOffset? NextOpenAfter { get; private set; }
        public Task<AccountingPeriod?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(items.FirstOrDefault(p => p.Id == id));
        public async Task<AccountingPeriod> RequireAsync(string id, CancellationToken ct = default) =>
            await FindAsync(id, ct) ?? throw new InvalidOperationException(id);
        public Task<AccountingPeriod?> FindByCodeAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(items.FirstOrDefault(p => p.PeriodCode == code));
        public Task<AccountingPeriod?> FindContainingAsync(DateTimeOffset date, CancellationToken ct = default) =>
            Task.FromResult(items.FirstOrDefault(p => p.StartUtc <= date && date < p.EndUtc));
        public Task<AccountingPeriod?> FindNextOpenAsync(DateTimeOffset afterUtc, CancellationToken ct = default)
        {
            NextOpenAfter = afterUtc;
            return Task.FromResult(items.Where(p => p.StartUtc >= afterUtc && p.State == PeriodState.Open)
                .OrderBy(p => p.StartUtc).FirstOrDefault());
        }
        public void Add(AccountingPeriod entity) => throw new NotSupportedException();
        public void AddRange(IEnumerable<AccountingPeriod> entities) => throw new NotSupportedException();
    }
    private sealed class Numbers : IJournalNumberAllocator
    {
        public Task<string> AllocateAsync(string tenantId, string periodCode, CancellationToken ct = default) =>
            Task.FromResult($"{periodCode}-00001");
    }
    private sealed class Entries : ILedgerRepository
    {
        public List<JournalEntry> Items { get; } = [];
        public Task<JournalEntry?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult<JournalEntry?>(null);
        public Task<JournalEntry> RequireAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JournalEntry?> FindInvoicePostingAsync(string id, CancellationToken ct = default) => Task.FromResult<JournalEntry?>(null);
        public void Add(JournalEntry entry) => Items.Add(entry);
        public void AddRange(IEnumerable<JournalEntry> entries) => Items.AddRange(entries);
    }
}
