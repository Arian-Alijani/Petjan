using System.Globalization;
using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;

namespace Animora.Persistence.Sqlite.Finance;

/// <summary>Atomically advances the same transactional sequence table used for invoice numbers.</summary>
public sealed class JournalNumberAllocator(AnimoraLocalDbContext db) : IJournalNumberAllocator
{
    public async Task<string> AllocateAsync(string tenantId, string periodCode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodCode);
        var sequence = await PipelineSql.NextSequenceAsync(db, $"journal:{tenantId}:{periodCode}", ct)
            .ConfigureAwait(false);
        return string.Create(CultureInfo.InvariantCulture, $"{periodCode}-{sequence:00000}");
    }
}
