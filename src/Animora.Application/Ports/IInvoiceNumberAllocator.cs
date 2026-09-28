namespace Animora.Application.Ports;

/// <summary>Allocates a tenant/year-scoped, gap-tolerant invoice number.</summary>
public interface IInvoiceNumberAllocator
{
    Task<string> AllocateAsync(
        string tenantId,
        DateTimeOffset issuedUtc,
        CancellationToken ct = default);
}
