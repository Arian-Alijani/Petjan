using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Cashier;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;

namespace Animora.LiveTest.Correctness;

/// <summary>
/// Point of sale and cashier: invoice draft → lines → discount → post → payments → void / refund, verified against the
/// authoritative <c>/finance/ledger</c> read; draft immutability; cash session open → movement → close; cash payment needs a drawer.
/// </summary>
public static class SalesScenarios
{
    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        new("Sales", "Invoice: draft → line → discount → post → two payments → Paid; totals match the ledger read", InvoiceHappyPathAsync),
        new("Sales", "Posted invoice is immutable (line add refused); overpayment refused", ImmutabilityAsync),
        new("Sales", "Void a posted unpaid invoice → ledger shows Void", VoidAsync),
        new("Sales", "Refund part of a paid invoice → ledger paid total drops; refund above paid is refused", RefundAsync),
        new("Sales", "Draft invoice delete → gone from ledger intent (second post refused)", DeleteDraftAsync),
        new("Cashier", "Open session → movement in/out → cash payment lands in drawer → close with variance note", CashierAsync),
    ];

    private static async Task<string> NewPostedInvoiceAsync(ScenarioContext c, long unit = 100_000, decimal qty = 2m)
    {
        var op = await c.Shared.OwnerPetAsync();
        var created = await c.AcceptAsync(c.A, new CreateBlankInvoiceCommand(op.OwnerId, null, op.PetId));
        var id = ScenarioContext.IdOf(created);
        await c.AcceptAsync(c.A, new AddCustomInvoiceLineCommand(id, c.Name("line"), unit, qty));
        await c.AcceptAsync(c.A, new PostInvoiceCommand(id));
        return id;
    }

    private static async Task InvoiceHappyPathAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var created = await c.AcceptAsync(c.A, new CreateBlankInvoiceCommand(op.OwnerId, null, op.PetId));
        var id = ScenarioContext.IdOf(created);
        await c.AcceptAsync(c.A, new AddCustomInvoiceLineCommand(id, c.Name("consult"), 100_000, 2m));       // 200 000
        await c.AcceptAsync(c.B, new SetInvoiceDiscountCommand(id, DiscountKind.Percent, 10m));               // −20 000 ⇒ 180 000
        await c.AcceptAsync(c.A, new PostInvoiceCommand(id));

        await c.AwaitInvoiceAsync(c.B, id, (i, _) => i.State == (int)InvoiceState.Posted, "Posted");
        await c.AcceptAsync(c.B, new RegisterPaymentCommand(id, PaymentMethod.Transfer, Money.Irt(80_000), "lt-ref-1"));
        var (partial, _) = await c.AwaitInvoiceAsync(c.A, id, (i, p) => p.Count == 1, "first payment recorded");
        c.Check(partial.State == (int)InvoiceState.PartiallyPaid, $"after a partial payment the invoice state is {partial.State}, expected PartiallyPaid ({(int)InvoiceState.PartiallyPaid}).");
        c.Check(partial.GrandTotalMinor == 180_000, $"grand total {partial.GrandTotalMinor}, expected 180000 (2×100000 less 10%).");
        c.Check(partial.PaidTotalMinor == 80_000 && partial.DueTotalMinor == 100_000, $"paid/due {partial.PaidTotalMinor}/{partial.DueTotalMinor}, expected 80000/100000.");

        await c.AcceptAsync(c.A, new RegisterPaymentCommand(id, PaymentMethod.Transfer, Money.Irt(100_000), "lt-ref-2"));
        var (paid, payments) = await c.AwaitInvoiceAsync(c.B, id, (i, p) => i.State == (int)InvoiceState.Paid, "Paid");
        c.Check(paid.DueTotalMinor == 0 && paid.PaidTotalMinor == 180_000, $"settled invoice paid/due {paid.PaidTotalMinor}/{paid.DueTotalMinor}, expected 180000/0.");
        c.Check(payments.Sum(p => p.AmountMinor) == 180_000, $"payment rows sum to {payments.Sum(p => p.AmountMinor)}, expected 180000.");
    }

    private static async Task ImmutabilityAsync(ScenarioContext c)
    {
        var id = await NewPostedInvoiceAsync(c, 50_000, 1m);
        await c.AwaitInvoiceAsync(c.B, id, (i, _) => i.State == (int)InvoiceState.Posted, "Posted");
        await c.RejectAsync(c.B, new AddCustomInvoiceLineCommand(id, c.Name("late-line"), 1_000, 1m));
        await c.RejectAsync(c.B, new SetInvoiceDiscountCommand(id, DiscountKind.Amount, 1_000m));
        await c.RejectAsync(c.A, new RegisterPaymentCommand(id, PaymentMethod.Transfer, Money.Irt(60_000), "lt-over"));
        var (inv, payments) = await c.AwaitInvoiceAsync(c.A, id, (i, _) => i.State == (int)InvoiceState.Posted, "still Posted and unpaid");
        c.Check(inv.GrandTotalMinor == 50_000 && inv.PaidTotalMinor == 0 && payments.Count == 0, "refused commands changed the invoice.");
    }

    private static async Task VoidAsync(ScenarioContext c)
    {
        var id = await NewPostedInvoiceAsync(c, 30_000, 1m);
        await c.AcceptAsync(c.B, new VoidInvoiceCommand(id, "lt void"));
        var (inv, _) = await c.AwaitInvoiceAsync(c.A, id, (i, _) => i.State == (int)InvoiceState.Void, "Void");
        c.Check(inv.VoidedUtc is not null, "voided invoice has no VoidedUtc.");
        await c.RejectAsync(c.A, new RegisterPaymentCommand(id, PaymentMethod.Transfer, Money.Irt(1_000), "lt-after-void"));
    }

    private static async Task RefundAsync(ScenarioContext c)
    {
        var id = await NewPostedInvoiceAsync(c, 40_000, 1m);
        await c.AcceptAsync(c.A, new RegisterPaymentCommand(id, PaymentMethod.Transfer, Money.Irt(40_000), "lt-pay"));
        await c.AwaitInvoiceAsync(c.A, id, (i, _) => i.State == (int)InvoiceState.Paid, "Paid");

        await c.RejectAsync(c.B, new RefundInvoiceCommand(id, Money.Irt(90_000), "lt too much"), "ADJUSTMENT_EXCEEDS_PAID");
        await c.AcceptAsync(c.B, new RefundInvoiceCommand(id, Money.Irt(15_000), "lt partial refund"));
        var (inv, _) = await c.AwaitInvoiceAsync(c.A, id, (i, _) => i.PaidTotalMinor == 25_000, "paid total reduced to 25000");
        c.Check(inv.GrandTotalMinor == 40_000, $"refund changed the grand total ({inv.GrandTotalMinor}).");
    }

    private static async Task DeleteDraftAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var created = await c.AcceptAsync(c.A, new CreateBlankInvoiceCommand(op.OwnerId, null, op.PetId));
        var id = ScenarioContext.IdOf(created);
        await c.AcceptAsync(c.A, new AddCustomInvoiceLineCommand(id, c.Name("doomed"), 1_000, 1m));
        await c.AcceptAsync(c.B, new DeleteDraftInvoiceCommand(id));
        await c.RejectAsync(c.A, new PostInvoiceCommand(id));
    }

    private static async Task CashierAsync(ScenarioContext c)
    {
        // Open a drawer for the logged-in user unless one is already open (then reuse and leave it open).
        var open = await c.SubmitAsync(c.A, new OpenCashSessionCommand(Money.Irt(50_000)));
        var weOpened = open.IsAccepted;
        if (!weOpened && open.Settlement?.ErrorCode != "CASH_SESSION_ALREADY_OPEN")
            throw ScenarioContext.Fail($"OpenCashSession failed unexpectedly: {open.Describe()}");
        if (!weOpened) c.Log("a cash session was already open for this user — reusing it, it will not be closed by this run");

        if (weOpened) await c.RejectAsync(c.B, new OpenCashSessionCommand(Money.Irt(1)), "CASH_SESSION_ALREADY_OPEN");

        await c.AcceptAsync(c.A, new RecordCashMovementCommand(CashMovementKind.FloatIn, Money.Irt(20_000), "lt float in"));
        await c.AcceptAsync(c.A, new RecordCashMovementCommand(CashMovementKind.PettyCashOut, Money.Irt(5_000), "lt petty cash"));
        await c.RejectAsync(c.A, new RecordCashMovementCommand(CashMovementKind.SalePayment, Money.Irt(1_000), "lt forged sale"));

        var id = await NewPostedInvoiceAsync(c, 10_000, 1m);
        await c.AcceptAsync(c.A, new RegisterPaymentCommand(id, PaymentMethod.Cash, Money.Irt(10_000)));
        await c.AwaitInvoiceAsync(c.B, id, (i, _) => i.State == (int)InvoiceState.Paid, "paid in cash");

        if (weOpened)
        {
            // Expected = 50000 + 20000 − 5000 + 10000 = 75000; count 70000 → variance −5000 needs a note.
            await c.RejectAsync(c.A, new CloseCashSessionCommand(Money.Irt(70_000), null));
            await c.AcceptAsync(c.A, new CloseCashSessionCommand(Money.Irt(70_000), "lt variance note"));
            await c.RejectAsync(c.A, new RecordCashMovementCommand(CashMovementKind.FloatIn, Money.Irt(1_000), "lt after close"), "CASH_SESSION_CLOSED");
            var after = await c.SubmitAsync(c.A, new RegisterPaymentCommand(id, PaymentMethod.Cash, Money.Irt(1)));
            c.Check(!after.IsAccepted, "a cash payment was accepted with no open cash session.");
        }
    }
}
