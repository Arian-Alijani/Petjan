using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;

namespace Animora.Persistence.Sqlite.Auditing;

/// <summary>Enlists append-only audit rows in the scoped local unit of work.</summary>
public sealed class AuditWriter(AnimoraLocalDbContext db, IUnitOfWork unitOfWork) : IAuditWriter
{
    public void Enlist(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        db.Set<AuditEntry>().Add(AuditEntry.Create(record));
    }

    public async Task<Result> WriteAsync(AuditRecord record, CancellationToken ct = default)
    {
        Enlist(record);
        var saved = await unitOfWork.SaveAsync(ct).ConfigureAwait(false);
        return saved.IsSuccess
            ? Result.Ok()
            : Result.Fail(ErrorCodes.AuditWriteFailed, saved.Detail ?? saved.Error.Code);
    }
}
