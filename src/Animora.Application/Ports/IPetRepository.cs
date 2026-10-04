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

    /// <summary>
    /// Every pet of the owner whose (trimmed) name equals <paramref name="name"/>, oldest first. Lets a caller
    /// tell an unambiguous single match from several same-named animals (walk-in disambiguation, V08).
    /// </summary>
    Task<IReadOnlyList<Pet>> ListByOwnerAndNameAsync(string ownerId, string name, CancellationToken ct = default);

    /// <summary>
    /// Every live (non-tombstoned) pet belonging to the owner, tracked so callers can mutate them
    /// in the same unit of work. Backs the owner soft-delete cascade (C01).
    /// </summary>
    Task<IReadOnlyList<Pet>> ListByOwnerAsync(string ownerId, CancellationToken ct = default);

    void Add(Pet entity);
    void AddRange(IEnumerable<Pet> entities);
}
