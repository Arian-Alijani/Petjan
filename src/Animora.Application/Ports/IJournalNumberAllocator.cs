namespace Animora.Application.Ports;

/// <summary>Allocates an atomic tenant/period-scoped journal sequence inside the posting transaction.</summary>
public interface IJournalNumberAllocator
{
    Task<string> AllocateAsync(string tenantId, string periodCode, CancellationToken ct = default);
}
