using Animora.Domain.Patients;

namespace Animora.Application.Ports;

public interface IPetRepository
{
    Task<Pet?> FindAsync(string id, CancellationToken ct = default);
    Task<Pet> RequireAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Finds an owner's pet whose (trimmed) name equals <paramref name="name"/>, or
    /// <see langword="null"/>. Backs walk-in de-duplication (C02): a matching owner keeps their pet
    /// instead of a duplicate being created.
    /// </summary>
    Task<Pet?> FindByOwnerAndNameAsync(string ownerId, string name, CancellationToken ct = default);

    void Add(Pet entity);
    void AddRange(IEnumerable<Pet> entities);
}
