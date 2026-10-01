using System.Text.Json;
using Animora.Application.Correlation;
using Animora.Application.Outbox;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;

namespace Animora.Persistence.Sqlite.Outbox;

/// <summary>
/// The DB-backed transactional outbox. <see cref="EnqueueAsync"/> only <c>Add</c>s — it never calls
/// <c>SaveChanges</c>, so the message rides the caller's business transaction (ARCHITECTURE.md §12.1).
/// </summary>
public sealed class LocalOutbox(
    AnimoraLocalDbContext db,
    IClock clock,
    ICorrelationContext correlation) : IOutbox
{
    public Task<Result> EnqueueAsync(string messageType, object payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(payload);
        var json = JsonSerializer.Serialize(payload, payload.GetType());
        db.Add(OutboxMessage.Create(messageType, json, correlation.CurrentId, clock.UtcNow,
            correlation.Current?.UserId));
        return Task.FromResult(Result.Ok());
    }
}
