using Animora.Application.UseCases.Finance;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Domain.Finance;
using Animora.Domain.Sales;

namespace Animora.LiveTest.Correctness;

/// <summary>Expenses (post / reverse), cheques (register → portfolio → deposit → cleared, edit, delete, invalid transition) and the finance read.</summary>
public static class FinanceScenarios
{
    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        new("Finance", "Expense: post → reverse; double reverse and bad category refused", ExpenseAsync),
        new("Cheques", "Inbound cheque: register → edit → portfolio → deposit → cleared; illegal transition refused", ChequeLifecycleAsync),
        new("Cheques", "Cheque draft delete; counterparty is mandatory", ChequeDeleteAsync),
        new("Finance", "/finance/ledger answers with a well-formed page and a stable cursor", LedgerReadAsync),
    ];

    private static async Task ExpenseAsync(ScenarioContext c)
    {
        var posted = await c.AcceptAsync(c.A, new AddExpenseCommand(ExpenseCategories.Supplies, c.Name("expense"), 25_000, DateTimeOffset.UtcNow, PaymentMethod.Transfer, null, null, "lt expense"));
        var id = ScenarioContext.IdOf(posted);
        c.Check(id.Length > 0, "expense settled without an entity id.");
        var reversed = await c.AcceptAsync(c.B, new ReverseExpenseCommand(id, "lt reverse"));
        c.Check(ScenarioContext.IdOf(reversed) != id, "the reversal must be a NEW expense row, not a mutation of the original.");
        await c.RejectAsync(c.A, new ReverseExpenseCommand(id, "lt reverse twice"));
        await c.RejectAsync(c.A, new AddExpenseCommand("NOT_A_CATEGORY", c.Name("bad-expense"), 1_000, DateTimeOffset.UtcNow));
        await c.RejectAsync(c.A, new AddExpenseCommand(ExpenseCategories.Other, c.Name("neg-expense"), -5, DateTimeOffset.UtcNow));
    }

    private static async Task ChequeLifecycleAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var number = c.Code();
        var created = await c.AcceptAsync(c.A, new RegisterChequeCommand(ChequeDirection.Inbound, number, 500_000, DateTimeOffset.UtcNow.AddDays(10),
            DateTimeOffset.UtcNow, OwnerId: op.OwnerId, BankName: "lt-bank"));
        var id = ScenarioContext.IdOf(created);

        await c.AcceptAsync(c.B, new UpdateChequeMetadataCommand(id, number, DateTimeOffset.UtcNow.AddDays(12), "lt-bank-2", null, null, null, "lt note"));
        await c.AcceptAsync(c.A, new TransitionChequeCommand(id, ChequeTransition.MoveToPortfolio));
        await c.RejectAsync(c.A, new TransitionChequeCommand(id, ChequeTransition.MarkCleared), "CHEQUE_INVALID_TRANSITION");
        await c.AcceptAsync(c.B, new TransitionChequeCommand(id, ChequeTransition.Deposit));
        await c.AcceptAsync(c.A, new TransitionChequeCommand(id, ChequeTransition.MarkCleared));
        await c.RejectAsync(c.B, new TransitionChequeCommand(id, ChequeTransition.Cancel, "lt too late"), "CHEQUE_INVALID_TRANSITION");
    }

    private static async Task ChequeDeleteAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var created = await c.AcceptAsync(c.A, new RegisterChequeCommand(ChequeDirection.Inbound, c.Code(), 10_000, DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow, OwnerId: op.OwnerId));
        await c.AcceptAsync(c.B, new DeleteChequeDraftCommand(ScenarioContext.IdOf(created)));
        await c.RejectAsync(c.A, new RegisterChequeCommand(ChequeDirection.Inbound, c.Code(), 10_000, DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow), "CHEQUE_COUNTERPARTY_REQUIRED");
    }

    private static async Task LedgerReadAsync(ScenarioContext c)
    {
        var (status, body, elapsed) = await c.A.GetAsync("finance/ledger?since=0&take=5", c.Ct);
        c.Log($"GET finance/ledger → {(status is null ? "no response" : ((int)status).ToString())} in {elapsed.TotalMilliseconds:0} ms");
        c.Check(status == System.Net.HttpStatusCode.OK, $"finance/ledger answered {status}: {body}");
        var page = System.Text.Json.JsonSerializer.Deserialize<Animora.Contracts.Wire.Sync.FinanceReadPage>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        c.Check(page is not null, "finance/ledger returned an unparsable page.");
        c.Check(page!.NextCursorTicks >= 0, "negative cursor.");
        var (s2, _, _) = await c.B.GetAsync("finance/ledger?since=0&take=5", c.Ct);
        c.Check(s2 == System.Net.HttpStatusCode.OK, $"second client finance/ledger answered {s2}.");
    }
}
