namespace Animora.Contracts.Wire.Commands;

/// <summary>
/// The single wire shape a business command travels in, client → server (ARCHITECTURE.md §8.3, CONVENTIONS §6 item 4).
/// It carries <b>intent</b>, never replicated state, and is the only command envelope either side speaks.
/// </summary>
/// <param name="ContractVersion">
/// Wire-contract version of the payload; defaults to <see cref="WireContract.Version"/> when built through
/// <see cref="Create"/>. An unsupported version triggers a controlled resync, never a silent mis-map (§8.6).
/// </param>
/// <param name="OperationId">Stable business-command identity (ULID) — the idempotency key with the tenant (A-RULE 005).</param>
/// <param name="TenantId">Owning tenant. Ignored server-side and re-resolved from membership (A-RULE 012).</param>
/// <param name="UserId">Acting user on the device that produced the command.</param>
/// <param name="DeviceId">Acting device.</param>
/// <param name="CommandType">Catalog name from <see cref="CommandTypes"/> (<c>&lt;context&gt;.&lt;aggregate&gt;.&lt;verb&gt;</c>).</param>
/// <param name="AggregateType">The aggregate the command targets.</param>
/// <param name="AggregateId">Target aggregate id, when the command names an existing aggregate.</param>
/// <param name="PayloadJson">Canonical JSON of the command payload.</param>
/// <param name="CommandFingerprint">SHA-256 hex over <c>CommandType + separator + canonical payload</c> (A-RULE 019).</param>
/// <param name="CreatedAtUtc">Client-local creation instant. Informational only — never a sync/ordering authority (A-RULE 011).</param>
/// <param name="LocalSequence">Monotonic per-installation sequence; the only local ordering key the processor honours.</param>
/// <param name="CausationId">The command/event that caused this one, when known (A-RULE 021).</param>
/// <param name="DependsOnOperationId">Explicit dependency on another command settling first (A-RULE 021/022).</param>
/// <param name="ExpectedAggregateVersion">The opaque version the caller expects, when known. Detection only (A-RULE 004/010).</param>
public sealed record CommandEnvelope(
    int ContractVersion,
    string OperationId,
    string TenantId,
    string UserId,
    string DeviceId,
    string CommandType,
    string AggregateType,
    string? AggregateId,
    string PayloadJson,
    string CommandFingerprint,
    DateTimeOffset CreatedAtUtc,
    long LocalSequence,
    string? CausationId,
    string? DependsOnOperationId,
    long? ExpectedAggregateVersion)
{
    /// <summary>
    /// Builds an envelope stamped with the current <see cref="WireContract.Version"/> — the default path.
    /// A positional default on <c>ContractVersion</c> is not expressible in C# (optional parameters must be
    /// trailing), so the version default is realized here instead of on the record shape.
    /// </summary>
    public static CommandEnvelope Create(
        string operationId,
        string tenantId,
        string userId,
        string deviceId,
        string commandType,
        string aggregateType,
        string? aggregateId,
        string payloadJson,
        string commandFingerprint,
        DateTimeOffset createdAtUtc,
        long localSequence,
        string? causationId = null,
        string? dependsOnOperationId = null,
        long? expectedAggregateVersion = null) =>
        new(WireContract.Version, operationId, tenantId, userId, deviceId, commandType, aggregateType,
            aggregateId, payloadJson, commandFingerprint, createdAtUtc, localSequence, causationId,
            dependsOnOperationId, expectedAggregateVersion);
}
