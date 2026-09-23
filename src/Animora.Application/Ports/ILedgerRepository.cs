using Animora.Domain.Finance;

namespace Animora.Application.Ports;

public interface ILedgerRepository
{
    Task<JournalEntry?> FindAsync(string id, CancellationToken ct = default);
    Task<JournalEntry> RequireAsync(string id, CancellationToken ct = default);
    void Add(JournalEntry entity);
    void AddRange(IEnumerable<JournalEntry> entities);
}
