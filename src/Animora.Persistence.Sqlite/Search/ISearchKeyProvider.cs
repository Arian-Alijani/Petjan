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
    public string Build(Owner e) => PersianText.Normalize(string.Join(
        ' ', e.FullName, e.PhonePrimary, e.PhoneSecondary ?? string.Empty, e.NationalId ?? string.Empty));
}

/// <summary>Pet key = name + breed + microchip, normalized by <see cref="PersianText.Normalize"/>.</summary>
public sealed class PetSearchKeyProvider : ISearchKeyProvider<Pet>
{
    public string Build(Pet e) => PersianText.Normalize(string.Join(
        ' ', e.Name, e.Breed ?? string.Empty, e.MicrochipCode ?? string.Empty));
}
