using Animora.Domain.Sales;

namespace Animora.Application.Ports;

/// <summary>Inputs needed while assembling a draft; implemented by the transactional local store.</summary>
public interface ISalesDraftData
{
    Task<SalesHandoff?> FindHandoffAsync(string source, string id, CancellationToken ct);
    Task<Insurer?> FindInsurerAsync(string id, CancellationToken ct);
    Task<SalesCatalogEntry?> FindCatalogEntryAsync(string id, CancellationToken ct);
}

public sealed record SalesHandoff(string SourceId, string OwnerId, string PetId, string LinesJson,
    bool AlreadyInvoiced, bool IsReady);
public sealed record SalesCatalogEntry(string Id, string? ProductId, string? VariantId,
    string Title, InvoiceLineKind Kind, long PriceMinor, string UnitCode,
    bool TrackStock = false, long OnHandScaled = 0);
