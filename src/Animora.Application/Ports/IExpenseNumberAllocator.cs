namespace Animora.Application.Ports;

/// <summary>Allocates a tenant/year-scoped, gap-tolerant expense number (mirrors the invoice allocator).</summary>
public interface IExpenseNumberAllocator
{
    Task<string> AllocateAsync(
        string tenantId,
        DateTimeOffset issuedUtc,
        CancellationToken ct = default);
}
