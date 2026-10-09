using System.Collections.Frozen;

namespace Animora.Server.Commands;

internal static class ReservedCommandTypes
{
    public static FrozenSet<string> All { get; } = new[]
    {
        "sales.adjustment.add", "inventory.receipt.post", "purchasing.order.submit",
        "finance.period.close", "finance.cheque.status",
    }.ToFrozenSet(StringComparer.Ordinal);
}
