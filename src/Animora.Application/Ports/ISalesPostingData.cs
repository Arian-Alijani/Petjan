using Animora.Domain.ValueObjects;

namespace Animora.Application.Ports;

/// <summary>Transactional handoff and stock facts for posting an invoice.</summary>
public interface ISalesPostingData
{
    bool AllowNegativeStock { get; }
    Task<long> GetOnHandScaledAsync(string productId, string variantId, CancellationToken ct);
    Task<Money> GetLatestReceiptCostAsync(string productId, string variantId, CancellationToken ct);
    Task<HandoffPostStatus> GetHandoffStatusAsync(string source, string sourceId, CancellationToken ct);
    Task<HandoffPostStatus> MarkHandoffInvoicedAsync(string source, string sourceId, string invoiceId, CancellationToken ct);

    /// <summary>
    /// The kind of the stay's <b>active (Ready)</b> handoff, or <see langword="null"/> when none is ready.
    /// At most one handoff per stay is ever Ready at a time — the prepaid room one is marked invoiced
    /// before checkout raises the final one — so this reliably tells posting whether it is settling the
    /// prepaid room (do not check out) or the final bill (attach + check out). Boarding only.
    /// </summary>
    Task<BoardingHandoffKind?> GetReadyBoardingHandoffKindAsync(string stayId, CancellationToken ct);
}

public enum HandoffPostStatus { Ready, AlreadyInvoiced, NotFound }
