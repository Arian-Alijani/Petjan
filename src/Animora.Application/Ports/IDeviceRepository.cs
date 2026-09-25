using Animora.Domain.Identity;

namespace Animora.Application.Ports;

public interface IDeviceRepository
{
    Task<Device?> FindAsync(string id, CancellationToken ct = default);
    void Add(Device entity);
}
