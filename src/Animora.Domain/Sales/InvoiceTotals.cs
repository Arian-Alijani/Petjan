using Animora.Domain.ValueObjects;

namespace Animora.Domain.Sales;

/// <summary>How an invoice-level discount is expressed.</summary>
public enum DiscountKind
{
    /// <summary>A percentage of the post-line-discount subtotal.</summary>
    Percent = 0,

    /// <summary>A fixed amount, given in minor units of the invoice currency.</summary>
    Amount = 1,
}

/// <summary>
/// The computed economics of an invoice, produced by <see cref="InvoiceTotals.Compute"/>.
/// </summary>
/// <param name="Subtotal">Σ of the lines' gross amounts, before any discount or tax.</param>
/// <param name="LineDiscountTotal">Σ of the lines' own discounts.</param>
/// <param name="InvoiceDiscount">The invoice-level discount, after resolving its <see cref="DiscountKind"/>.</param>
/// <param name="DiscountTotal"><c>LineDiscountTotal + InvoiceDiscount</c>.</param>
/// <param name="TaxTotal">Σ of the per-line tax on the base after <b>both</b> discounts.</param>
/// <param name="InsuranceCoverage">The insurer's share of the taxed, discounted total.</param>
/// <param name="GrandTotal"><c>Subtotal - DiscountTotal + TaxTotal - InsuranceCoverage</c>: what the payer owes.</param>
public readonly record struct InvoiceTotalsSnapshot(
    Money Subtotal,
    Money LineDiscountTotal,
    Money InvoiceDiscount,
    Money DiscountTotal,
    Money TaxTotal,
    Money InsuranceCoverage,
    Money GrandTotal);

/// <summary>
/// The <b>single</b> totals algorithm of the product. Nothing else computes invoice economics:
/// <see cref="Invoice"/> delegates here, and every later layer (C07 cashier, D07 server posting,
/// E-track reports) recomputes with this same function so client and server can never disagree.
/// </summary>
/// <remarks>
/// <para>The algorithm, in order:</para>
/// <code>
/// Subtotal          = Σ LineGross
/// LineDiscountTotal = Σ LineDiscount                        (per-line percentages)
/// InvoiceDiscount   = Percent ? (Subtotal - LineDiscountTotal).ApplyPercent(DiscountValue)
///                             : Money(DiscountValue minor units)
/// DiscountTotal     = LineDiscountTotal + InvoiceDiscount
/// Taxable base      = line net after BOTH the line discount and the pro-rated invoice discount
/// TaxTotal          = Σ (taxable base × TaxPercent)
/// InsuranceCoverage = (Subtotal - DiscountTotal + TaxTotal).ApplyPercent(InsurancePercent)
/// GrandTotal        = Subtotal - DiscountTotal + TaxTotal - InsuranceCoverage
/// </code>
/// <para>
/// Rounding happens only inside <see cref="Money.ApplyPercent(decimal)"/> (away from zero, on the
/// minor unit) — the product's single rounding boundary. The invoice discount is spread across the
/// lines by largest-remainder allocation weighted by each line's post-line-discount net, so the
/// allocations sum back to exactly the invoice discount and no minor unit is lost or invented
/// (the same guarantee <see cref="Money.Allocate(int)"/> gives, which is used verbatim whenever the
/// weights carry no information).
/// </para>
/// </remarks>
public static class InvoiceTotals
{
    /// <summary>
    /// Computes every total of an invoice from its lines and invoice-level parameters.
    /// </summary>
    /// <param name="lines">The charged lines; an empty list yields all-zero totals.</param>
    /// <param name="discountKind">How <paramref name="discountValue"/> is to be read.</param>
    /// <param name="discountValue">A percentage, or a fixed amount in minor units.</param>
    /// <param name="insurancePercent">The insurer's share, in percent, of the taxed discounted total.</param>
    /// <param name="currency">ISO-4217 code every amount of the invoice is expressed in.</param>
    public static InvoiceTotalsSnapshot Compute(
        IReadOnlyList<InvoiceLine> lines,
        DiscountKind discountKind,
        decimal discountValue,
        decimal insurancePercent,
        string currency)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var grossMinor = 0L;
        var lineDiscountMinor = 0L;
        var netWeights = new long[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var gross = line.LineGross.Minor;
            var discount = line.LineDiscount.Minor;

            grossMinor = checked(grossMinor + gross);
            lineDiscountMinor = checked(lineDiscountMinor + discount);
            netWeights[i] = gross - discount;
        }

        var subtotal = new Money(grossMinor, currency);
        var lineDiscountTotal = new Money(lineDiscountMinor, currency);
        var netAfterLineDiscount = new Money(grossMinor - lineDiscountMinor, currency);

        var invoiceDiscount = discountKind == DiscountKind.Percent
            ? netAfterLineDiscount.ApplyPercent(discountValue)
            : new Money((long)Math.Round(discountValue, MidpointRounding.AwayFromZero), currency);

        var discountTotal = lineDiscountTotal.Add(invoiceDiscount);

        // The invoice discount lands on the individual lines before tax, so a taxed and an untaxed
        // line each carry their fair share of it.
        var shares = AllocateByWeight(invoiceDiscount, netWeights);

        var taxMinor = 0L;
        for (var i = 0; i < lines.Count; i++)
        {
            var taxableBase = new Money(netWeights[i] - shares[i], currency);
            taxMinor = checked(taxMinor + taxableBase.ApplyPercent(lines[i].TaxPercent).Minor);
        }

        var taxTotal = new Money(taxMinor, currency);
        var taxedNet = subtotal.Subtract(discountTotal).Add(taxTotal);
        var insuranceCoverage = taxedNet.ApplyPercent(insurancePercent);

        return new InvoiceTotalsSnapshot(
            subtotal,
            lineDiscountTotal,
            invoiceDiscount,
            discountTotal,
            taxTotal,
            insuranceCoverage,
            taxedNet.Subtract(insuranceCoverage));
    }

    /// <summary>
    /// The amount still owed: <c>GrandTotal - PaidTotal - Σ adjustments</c>, where a credit note or
    /// write-off carries a positive amount (it reduces the debt) and a refund a negative one (money
    /// returned re-opens it) — the compensating-entry convention of A-RULE 002/003.
    /// </summary>
    public static Money ComputeDue(
        Money grandTotal,
        Money paidTotal,
        IReadOnlyList<InvoiceAdjustment> adjustments)
    {
        ArgumentNullException.ThrowIfNull(adjustments);

        var adjustedMinor = 0L;
        for (var i = 0; i < adjustments.Count; i++)
        {
            adjustedMinor = checked(adjustedMinor + adjustments[i].Amount.Minor);
        }

        return grandTotal
            .Subtract(paidTotal)
            .Subtract(new Money(adjustedMinor, grandTotal.Currency));
    }

    /// <summary>
    /// Splits <paramref name="amount"/> across <paramref name="weights"/> by largest remainder: the
    /// shares sum back to exactly <paramref name="amount"/>, so no minor unit is lost or invented.
    /// When the weights carry no information (all zero) the split degrades to the equal-share
    /// <see cref="Money.Allocate(int)"/>.
    /// </summary>
    private static long[] AllocateByWeight(Money amount, long[] weights)
    {
        var count = weights.Length;
        if (count == 0 || amount.IsZero)
        {
            return new long[count];
        }

        var totalWeight = 0L;
        for (var i = 0; i < count; i++)
        {
            totalWeight = checked(totalWeight + weights[i]);
        }

        if (totalWeight == 0L)
        {
            var equal = amount.Allocate(count);
            var flat = new long[count];
            for (var i = 0; i < count; i++)
            {
                flat[i] = equal[i].Minor;
            }

            return flat;
        }

        var shares = new long[count];
        var remainders = new decimal[count];
        var distributed = 0L;

        for (var i = 0; i < count; i++)
        {
            var exact = (decimal)amount.Minor * weights[i] / totalWeight;
            var whole = decimal.Truncate(exact);
            shares[i] = (long)whole;
            remainders[i] = exact - whole;
            distributed = checked(distributed + shares[i]);
        }

        // Hand the leftover minor units to the largest fractional remainders, one each.
        var leftover = amount.Minor - distributed;
        var step = Math.Sign(leftover);
        for (var handed = 0L; handed < Math.Abs(leftover); handed++)
        {
            var best = -1;
            for (var i = 0; i < count; i++)
            {
                var better = step > 0
                    ? best < 0 || remainders[i] > remainders[best]
                    : best < 0 || remainders[i] < remainders[best];
                if (better)
                {
                    best = i;
                }
            }

            shares[best] += step;
            remainders[best] -= step;
        }

        return shares;
    }
}
