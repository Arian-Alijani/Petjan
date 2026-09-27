using Animora.Domain.Patients;

namespace Animora.Application.Ports;

public interface IOwnerRepository
{
    Task<Owner?> FindAsync(string id, CancellationToken ct = default);
    Task<Owner> RequireAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Finds an owner by their <b>normalized</b> primary phone, or <see langword="null"/> when none
    /// matches. Backs walk-in de-duplication (C02): the caller passes <c>Owner.NormalizePhone(raw)</c>.
    /// </summary>
    Task<Owner?> FindByPhoneAsync(string normalizedPhone, CancellationToken ct = default);

    void Add(Owner entity);
    void AddRange(IEnumerable<Owner> entities);
}
