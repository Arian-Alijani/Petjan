using Animora.Domain.Finance;
using Animora.Domain.ValueObjects;
using Animora.Domain.Results;
using Xunit;
namespace Animora.Server.Tests.HandlersS10;
public sealed class LedgerImmutabilityTests
{
    [Fact] public void JournalEntries_NoUpdatePath()
    {
        var now = DateTimeOffset.UtcNow;
        var result = JournalEntry.Create("tenant", "JV-1", "1405", JournalSource.Sales,
            [new JournalLine("a", AccountCodes.Cash, EntrySide.Debit, Money.Irt(100), null, null),
             new JournalLine("b", AccountCodes.SalesRevenue, EntrySide.Credit, Money.Irt(90), null, null)], "Memo", "user", now);
        Assert.Equal(ErrorCodes.JournalUnbalanced, result.Error);
        Assert.DoesNotContain(typeof(JournalEntry).GetMethods(), m => m.DeclaringType == typeof(JournalEntry)
            && m.Name is "Update" or "Edit" or "Delete");
        Assert.Contains(typeof(JournalEntry).GetMethods(), m => m.Name == "CreateReversal");
    }
}
