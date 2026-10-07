using Animora.Domain.Purchasing;

namespace Animora.Application.Ports;

/// <summary>Write port for the supplier directory (C06): get-or-create by name and append-only add.</summary>
public interface ISupplierRepository
{
    /// <summary>The tenant's supplier with exactly this (trimmed) name, tracked; <see langword="null"/> when none.</summary>
    Task<Supplier?> FindByNameAsync(string name, CancellationToken ct = default);

    void Add(Supplier supplier);
}
