using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// Relay state of an outbox row (ARCHITECTURE.md §12.1). The atomic claim moves <c>Pending</c> →
/// <c>Claimed</c> → <c>Processing</c>; a failure schedules <c>Retry</c>, and the retry budget's end is
/// <c>DeadLettered</c> — never silently dropped.
/// </summary>
public enum OutboxStatus
{
    Pending,
    Claimed,
    Processing,
    Succeeded,
    Retry,
    DeadLettered,
}

/// <summary>
/// One durable outbox message: a domain event (or other at-least-once effect) captured in the same
/// transaction as the state change that produced it. Never replicated.
/// </summary>
[LocalOnly]
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public string Id { get; private set; } = string.Empty;
    public string MessageType { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public OutboxStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ClaimedUntilUtc { get; private set; }
    public string? ClaimToken { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? NextAttemptUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? CorrelationId { get; private set; }

    /// <summary>Appends a new message in the caller's transaction (never calls <c>SaveChanges</c>).</summary>
    internal static OutboxMessage Create(string messageType, string payloadJson, string? correlationId,
        DateTimeOffset nowUtc) => new()
    {
        Id = IdGenerator.NewId(nowUtc),
        MessageType = messageType,
        PayloadJson = payloadJson,
        Status = OutboxStatus.Pending,
        CreatedAtUtc = nowUtc,
        CorrelationId = correlationId,
    };

    /// <summary>Marks a relayed message succeeded and releases its claim.</summary>
    internal void MarkSucceeded(DateTimeOffset nowUtc)
    {
        Attempts++;
        Status = OutboxStatus.Succeeded;
        ProcessedAtUtc = nowUtc;
        ClearClaim();
    }

    /// <summary>
    /// Records a relay failure with backoff; once the retry budget is spent the message is dead-lettered with
    /// its last error preserved — never silently dropped (ARCHITECTURE.md §12.1).
    /// </summary>
    internal void MarkFailed(string errorCode, TimeSpan backoff, DateTimeOffset nowUtc, bool exhausted)
    {
        Attempts++;
        LastErrorCode = errorCode;
        NextAttemptUtc = nowUtc + backoff;
        Status = exhausted ? OutboxStatus.DeadLettered : OutboxStatus.Retry;
        ClearClaim();
    }

    private void ClearClaim()
    {
        ClaimToken = null;
        ClaimedUntilUtc = null;
    }
}
