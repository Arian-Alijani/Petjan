using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class DeviceRepository(AnimoraLocalDbContext db) : IDeviceRepository
{
    public Task<Device?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Device>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public void Add(Device entity) => db.Set<Device>().Add(entity);
}
