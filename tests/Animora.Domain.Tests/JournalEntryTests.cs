using System;
using System.Collections.Generic;
using System.Reflection;
using Animora.Domain.Primitives;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The append-only balanced double entry: an unbalanced, one-line or mixed-currency entry is
/// refused; a reversal mirrors the original so the two net to zero per account; and the type
/// exposes no public setter (A-RULE 002/003 · R-B22).
/// </summary>
public sealed class JournalEntryTests
{
    private static readonly DateTimeOffset Posted = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static JournalLine Debit(string account, long minor, string currency = "IRT") =>
        new(IdGenerator.NewId(Posted), account, EntrySide.Debit, Money.Of(minor, currency), null, null);

    private static JournalLine Credit(string account, long minor, string currency = "IRT") =>
        new(IdGenerator.NewId(Posted), account, EntrySide.Credit, Money.Of(minor, currency), null, null);

    private static Result<JournalEntry> Create(IReadOnlyList<JournalLine> lines) =>
        JournalEntry.Create("T1", "JE-1", "1405-07", JournalSource.Manual, lines, "memo", "U1", Posted);

    [Fact]
    public void UnbalancedEntry_IsRefused()
    {
        var result = Create([Debit(AccountCodes.CashOnHand, 1_000), Credit(AccountCodes.ServiceRevenue, 900)]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.JournalUnbalanced, result.Error);
    }

    [Fact]
    public void SingleLine_IsRefused()
    {
        var result = Create([Debit(AccountCodes.CashOnHand, 1_000)]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.JournalTooFewLines, result.Error);
    }

    [Fact]
    public void MixedCurrency_IsRefused()
    {
        var result = Create([Debit(AccountCodes.CashOnHand, 1_000, "IRT"), Credit(AccountCodes.ServiceRevenue, 1_000, "USD")]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.JournalCurrencyMixed, result.Error);
    }

    [Fact]
    public void NonPositiveLineAmount_IsRefused()
    {
        var result = Create([Debit(AccountCodes.CashOnHand, 0), Credit(AccountCodes.ServiceRevenue, 0)]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.JournalLineAmountInvalid, result.Error);
    }

    [Fact]
    public void TenantDefinedAccount_IsAllowedBecauseTheDefaultChartHasNoHeader()
    {
        // Every default-chart account is postable, so ACCOUNT_NOT_POSTABLE cannot be reached through
        // the default chart; a code absent from it is treated as a tenant-defined account the write
        // path resolves against the store, and is therefore accepted here.
        Assert.All(ChartOfAccounts.Default, e => Assert.True(e.IsPostable));

        var result = Create([Debit("9999", 1_000), Credit(AccountCodes.ServiceRevenue, 1_000)]);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void BalancedEntry_SucceedsAndRecordsTotalDebit()
    {
        var entry = Create([Debit(AccountCodes.CashOnHand, 1_000), Credit(AccountCodes.ServiceRevenue, 1_000)]).Value!;

        Assert.Equal(Money.Irt(1_000), entry.TotalDebit);
        Assert.Equal("IRT", entry.Currency);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public void CreateReversal_MirrorsAndNetsToZeroPerAccount()
    {
        var clock = new FakeClock(Posted);
        var original = Create([Debit(AccountCodes.CashOnHand, 5_000), Credit(AccountCodes.ServiceRevenue, 5_000)]).Value!;

        var reversal = JournalEntry.CreateReversal(original, "wrong amount", clock).Value!;

        Assert.True(reversal.IsReversal);
        Assert.Equal(original.Id, reversal.ReversesEntryId);
        Assert.Contains("wrong amount", reversal.Memo);
        Assert.Equal(Money.Irt(5_000), reversal.TotalDebit);

        // Combined effect per account nets to zero: original debit vs reversal credit, and vice versa.
        var net = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in original.Lines)
        {
            net[line.AccountCode] = net.GetValueOrDefault(line.AccountCode) + line.SignedMinor;
        }

        foreach (var line in reversal.Lines)
        {
            net[line.AccountCode] = net.GetValueOrDefault(line.AccountCode) + line.SignedMinor;
        }

        Assert.All(net.Values, value => Assert.Equal(0L, value));
    }

    [Fact]
    public void JournalEntry_ExposesNoPublicSetter()
    {
        foreach (var property in typeof(JournalEntry).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            // The inherited SyncableEntity transport properties are the store's bookkeeping; every
            // property JournalEntry declares itself must be init-only (append-only forever).
            // An init-only setter is construction-time only and therefore allowed; a mutable one is not.
            if (property.DeclaringType != typeof(JournalEntry)) continue;
            var setter = property.GetSetMethod(nonPublic: false);
            if (setter is null) continue;
            var modifiers = setter.ReturnParameter.GetRequiredCustomModifiers();
            Assert.Contains(modifiers, m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        }
    }
}
