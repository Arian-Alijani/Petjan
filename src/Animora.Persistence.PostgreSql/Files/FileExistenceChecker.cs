using Animora.Application.Ports;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Files;

public sealed class FileExistenceChecker(AnimoraServerDbContext db) : IFileExistenceChecker
{
    public Task<bool> IsAvailableAsync(string tenantId, string fileId, CancellationToken ct = default) =>
        db.FileObjects.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == fileId && x.State == FileState.Available, ct);
}
