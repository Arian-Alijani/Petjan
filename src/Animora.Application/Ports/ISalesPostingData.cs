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
}

public enum HandoffPostStatus { Ready, AlreadyInvoiced, NotFound }
