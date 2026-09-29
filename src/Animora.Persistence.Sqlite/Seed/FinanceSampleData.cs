using Animora.Application.Ports;
using Animora.Domain.Cashier;
using Animora.Domain.Finance;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>Optional, marker-guarded financial examples. The bank catalog stays static in BankDirectory.</summary>
public sealed class FinanceSampleData(AnimoraLocalDbContext db, ILocalTenantContext tenant, IClock clock)
{
    public const string SampleKey = "finance.sample";
    private const string SeedUser = "U-SEED";

    public bool AlreadySeeded() => db.Set<Cheque>().IgnoreQueryFilters().Any() ||
        db.Set<CashSession>().IgnoreQueryFilters().Any();

    public Result Populate()
    {
        var now = clock.UtcNow;
        var owners = db.Set<Owner>().AsNoTracking().OrderBy(o => o.Id).Take(12).ToArray();
        // A demo store without sample owners cannot have inbound cheques with invented counterparties.
        if (owners.Length == 0) return Result.Ok();
        var suppliers = db.Set<Animora.Domain.Purchasing.Supplier>().AsNoTracking()
            .OrderBy(s => s.Id).Take(8).ToArray();
        if (suppliers.Length == 0) return Result.Ok();
        var rows = new List<Cheque>(24);
        var banks = BankDirectory.All;
        for (var i = 0; i < 24; i++)
        {
            var direction = i % 2 == 0 ? ChequeDirection.Inbound : ChequeDirection.Outbound;
            var issued = now.AddDays(-50 + i);
            // 4 weeks past and 8 weeks ahead, yielding due, overdue and bounced alerts.
            var due = now.AddDays(-28 + i * 4);
            var created = Cheque.Register(tenant.TenantId, $"DEMO-{i + 1:0000}", direction,
                Money.Irt(500_000 + i * 75_000), due, issued,
                ownerId: direction == ChequeDirection.Inbound ? owners[(i / 2) % owners.Length].Id : null,
                supplierId: direction == ChequeDirection.Outbound ? suppliers[(i / 2) % suppliers.Length].Id : null,
                bankName: banks[i % banks.Count].Name, note: "Sample cheque");
            if (created.IsFailure) return Result.Fail(created.Error, created.Detail);
            var cheque = created.Value!;
            // Eight states, each traversed through the domain machine (including its audit trail).
            var state = i % 8;
            Result moved = Result.Ok();
            if (state is 1 or 2 or 3 or 4 or 5 or 7)
                moved = cheque.MoveToPortfolio(SeedUser, clock);
            if (moved.IsSuccess && state is 2 or 3 or 4 or 7)
                moved = cheque.Deposit(SeedUser, clock);
            if (moved.IsSuccess && state == 3) moved = cheque.MarkCleared(SeedUser, clock);
            if (moved.IsSuccess && state is 4 or 7) moved = cheque.MarkBounced(SeedUser, "Sample return", clock);
            if (moved.IsSuccess && state == 5) moved = cheque.Return(SeedUser, "Sample return", clock);
            if (moved.IsSuccess && state == 6) moved = cheque.Cancel(SeedUser, "Sample cancellation", clock);
            if (moved.IsSuccess && state == 7) moved = cheque.SettleInCash(SeedUser, clock);
            if (moved.IsFailure) return moved;
            rows.Add(cheque);
        }
        db.AddRange(rows);

        for (var i = 0; i < 3; i++)
        {
            var opened = CashSession.Open(tenant.TenantId, $"DEMO-CS-{i + 1:0000}",
                SeedUser, Money.Irt(2_000_000), now.AddDays(-3 + i));
            if (opened.IsFailure) return Result.Fail(opened.Error, opened.Detail);
            var session = opened.Value!;
            if (i < 2)
            {
                var begun = session.BeginClose(Money.Irt(2_000_000), null, clock, false);
                if (begun.IsFailure) return begun;
                var closed = session.Close(clock);
                if (closed.IsFailure) return closed;
            }
            db.Add(session);
        }
        return Result.Ok();
    }
}
