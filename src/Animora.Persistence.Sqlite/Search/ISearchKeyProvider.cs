using Animora.Application.Text;
using Animora.Domain.Patients;

namespace Animora.Persistence.Sqlite.Search;

/// <summary>Computes the normalized, persisted search index for an entity on every write (C01).</summary>
public interface ISearchKeyProvider<in TEntity>
{
    string Build(TEntity entity);
}

/// <summary>Owner key = full name + phones + national id, normalized by <see cref="PersianText.Normalize"/>.</summary>
public sealed class OwnerSearchKeyProvider : ISearchKeyProvider<Owner>
{
    public string Build(Owner e) => Compose(e.FullName, e.PhonePrimary, e.PhoneSecondary, e.NationalId);

    /// <summary>The key formula over raw columns; shared with the replica materializer so a synced row is searchable identically.</summary>
    public static string Compose(string fullName, string phonePrimary, string? phoneSecondary, string? nationalId) =>
        PersianText.Normalize(string.Join(' ', fullName, phonePrimary, phoneSecondary ?? string.Empty, nationalId ?? string.Empty));
}

/// <summary>Pet key = name + breed + microchip, normalized by <see cref="PersianText.Normalize"/>.</summary>
public sealed class PetSearchKeyProvider : ISearchKeyProvider<Pet>
{
    public string Build(Pet e) => Compose(e.Name, e.Breed, e.MicrochipCode);

    /// <summary>The key formula over raw columns; shared with the replica materializer so a synced row is searchable identically.</summary>
    public static string Compose(string name, string? breed, string? microchipCode) =>
        PersianText.Normalize(string.Join(' ', name, breed ?? string.Empty, microchipCode ?? string.Empty));
}
