using Animora.Domain.Cashier;

namespace Animora.Application.Ports;

/// <summary>Resolves the current cashier's open drawer without leaking persistence into use cases.</summary>
public interface ICashSessionContext
{
    Task<CashSession?> GetOpenSessionAsync(string tenantId, string userId, CancellationToken ct);
}
