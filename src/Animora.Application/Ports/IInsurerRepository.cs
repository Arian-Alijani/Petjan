using Animora.Domain.Sales;

namespace Animora.Application.Ports;

/// <summary>Write-side access to the insurer directory for the «پوشش بیمه‌ها» management commands.</summary>
public interface IInsurerRepository
{
    Task<Insurer?> FindAsync(string id, CancellationToken ct = default);

    void Add(Insurer entity);

    /// <summary>Whether another live insurer (not <paramref name="excludeId"/>) already uses <paramref name="name"/> (case-insensitive).</summary>
    Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default);
}
