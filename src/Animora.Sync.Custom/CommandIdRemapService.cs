using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Animora.Application.Commands;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Sync.Custom;

/// <summary>One server-allocated id created by a settled command (client-side mirror of the server's id-map item, which is internal there).</summary>
/// <param name="Slot">Creation slot; 0 is the primary aggregate.</param>
/// <param name="EntityType">Wire entity name.</param>
/// <param name="ServerEntityId">Server-allocated id.</param>
public sealed record CommandIdMapItem(int Slot, string EntityType, string ServerEntityId);

/// <summary>Body of <c>GET /api/commands/{operationId}/ids</c>.</summary>
/// <param name="Ids">Every id the command created — not only slot 0.</param>
public sealed record CommandIdMapResponse(IReadOnlyList<CommandIdMapItem> Ids);

/// <summary>
/// G05 provisional-id → server-id remap. A command that creates an aggregate is authored with a client-generated (provisional) id; once the
/// server settles it, dependent commands still waiting in the journal must travel with the server id. <see cref="Rewrite"/> replaces every
/// known provisional id (aggregate id, string values in the payload, dependency reference) and <b>recomputes</b> the fingerprint, because the
/// payload really changed (A-RULE 019). The map is tenant-scoped and seeded from the settlement's <c>ServerEntityId</c> and the
/// <c>/ids</c> endpoint. SYN-01: it is <b>write-through</b> to the durable <see cref="ICommandIdRemapStore"/> (when one is registered) and the
/// in-memory dictionary is only an L1 cache, hydrated once per tenant per process, so a creator that settled before an app restart still remaps
/// its dependents. With no store (unit/offline composition) it degrades to the previous memory-only behaviour; callers are unchanged.
/// </summary>
public sealed class CommandIdRemapService(IHttpClientFactory httpFactory, ServerEndpointOptions options,
    ICommandIdRemapStore? store = null, IClock? clock = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<(string TenantId, string LocalId), string> _map = new();
    private readonly ConcurrentDictionary<string, bool> _hydrated = new(StringComparer.Ordinal);

    /// <summary>Records that <paramref name="localId"/> is now <paramref name="serverId"/> for <paramref name="tenantId"/>.</summary>
    public void Register(string tenantId, string localId, string serverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        if (string.Equals(localId, serverId, StringComparison.Ordinal)) return;
        EnsureHydrated(tenantId);
        _map[(tenantId, localId)] = serverId;
        if (store is null) return;
        try
        {
            store.Upsert(tenantId, localId, serverId, clock?.UtcNow ?? DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The mapping is recoverable (GET /api/commands/{id}/ids re-learns it) so a failed durable write must never fail settlement.
            TraceLog.Warn($"CommandIdRemap: durable write failed for {localId}: {ex.Message}", "sync.push");
        }
    }

    /// <summary>Loads the tenant's persisted mappings into the L1 cache exactly once per process (retried when the store was unavailable).</summary>
    private void EnsureHydrated(string tenantId)
    {
        if (store is null || _hydrated.ContainsKey(tenantId)) return;
        try
        {
            var rows = store.LoadAll(tenantId);
            for (var i = 0; i < rows.Count; i++) _map.TryAdd((tenantId, rows[i].Key), rows[i].Value);
            _hydrated[tenantId] = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TraceLog.Warn($"CommandIdRemap: could not hydrate tenant map: {ex.Message}", "sync.push");
        }
    }

    /// <summary>The resolved server id for a provisional id, or <see langword="null"/> when unknown.</summary>
    public string? Resolve(string tenantId, string localId)
    {
        if (_map.TryGetValue((tenantId, localId), out var serverId)) return serverId;
        EnsureHydrated(tenantId); // L1 miss: fall back to the durable map once, then it is memory-only
        return _map.TryGetValue((tenantId, localId), out serverId) ? serverId : null;
    }

    /// <summary>
    /// Seeds the map from a settled creating command: slot 0 (the primary aggregate) maps the command's own <c>AggregateId</c>
    /// to <paramref name="settlement"/>'s <c>ServerEntityId</c>.
    /// </summary>
    public void Learn(PendingCommandRecord creator, SettlementResult settlement)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentNullException.ThrowIfNull(settlement);
        if (settlement.Outcome != SettlementOutcome.Accepted) return;
        if (creator.AggregateId is { Length: > 0 } local && settlement.ServerEntityId is { Length: > 0 } server)
            Register(creator.TenantId, local, server);
    }

    /// <summary>
    /// Fetches <b>all</b> ids the settled command created (<c>GET /api/commands/{operationId}/ids</c>) and, when the creating aggregate
    /// was authored with a provisional id, registers the slot-0 mapping. Returns the complete list (every slot).
    /// </summary>
    public async Task<Result<IReadOnlyList<CommandIdMapItem>>> FetchCreatedIdsAsync(
        string tenantId, string operationId, string? provisionalAggregateId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.ValidationFailed, "operationId is required.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/commands/{Uri.EscapeDataString(operationId)}/ids");
        request.Headers.TryAddWithoutValidation(WireContract.HeaderName, WireContract.Version.ToString(CultureInfo.InvariantCulture));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.RequestTimeout);
        try
        {
            var http = httpFactory.CreateClient(SyncFeedClient.HttpClientName);
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is 401 or 403) return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.Unauthorized);
            if ((int)response.StatusCode == 400) return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.ValidationFailed);
            if ((int)response.StatusCode is 429 or 503) return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.ServerBusy);
            if (!response.IsSuccessStatusCode) return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.NetworkUnavailable, $"Server answered {(int)response.StatusCode}.");

            var body = await response.Content.ReadFromJsonAsync<CommandIdMapResponse>(JsonOptions, ct).ConfigureAwait(false);
            if (body?.Ids is null)
                return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.SyncProtocolViolation, "Malformed id-map response.");

            var ids = body.Ids.OrderBy(i => i.Slot).ToArray();
            if (provisionalAggregateId is { Length: > 0 } && ids.FirstOrDefault(i => i.Slot == 0) is { } primary)
                Register(tenantId, provisionalAggregateId, primary.ServerEntityId);
            return Result<IReadOnlyList<CommandIdMapItem>>.Ok(ids);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.NetworkUnavailable, "The command server did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.NetworkUnavailable, ex.Message);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<CommandIdMapItem>>.Fail(ErrorCodes.SyncProtocolViolation, "Malformed id-map response.");
        }
    }

    /// <summary>
    /// Returns <paramref name="envelope"/> with every known provisional id replaced by its server id and the fingerprint recomputed.
    /// An envelope that references no known provisional id is returned unchanged (same instance, same fingerprint).
    /// </summary>
    public CommandEnvelope Rewrite(CommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        EnsureHydrated(envelope.TenantId);
        if (_map.IsEmpty) return envelope;

        var aggregateId = Remap(envelope.TenantId, envelope.AggregateId);
        var dependsOn = Remap(envelope.TenantId, envelope.DependsOnOperationId);
        var payload = RewritePayload(envelope.TenantId, envelope.PayloadJson, out var payloadChanged);

        if (!payloadChanged && aggregateId == envelope.AggregateId && dependsOn == envelope.DependsOnOperationId) return envelope;

        return envelope with
        {
            AggregateId = aggregateId,
            DependsOnOperationId = dependsOn,
            PayloadJson = payload,
            CommandFingerprint = CommandFingerprint.Compute(envelope.CommandType, payload),
        };
    }

    private string? Remap(string tenantId, string? id) => id is { Length: > 0 } && Resolve(tenantId, id) is { } server ? server : id;

    private string RewritePayload(string tenantId, string payloadJson, out bool changed)
    {
        changed = false;
        JsonNode? root;
        try { root = JsonNode.Parse(payloadJson); }
        catch (JsonException) { return payloadJson; }
        if (root is null) return payloadJson;

        var edited = false;
        var rewritten = Walk(tenantId, root, ref edited);
        if (!edited) return payloadJson;

        changed = true;
        return CommandFingerprint.CanonicalJson(rewritten);
    }

    // Whole-value match only: a provisional id embedded inside a longer string (e.g. a note) is user text, never an id reference.
    private JsonNode? Walk(string tenantId, JsonNode? node, ref bool edited)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToArray())
                {
                    var child = obj[key];
                    var replaced = Walk(tenantId, child, ref edited);
                    if (!ReferenceEquals(replaced, child)) obj[key] = replaced;
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    var replaced = Walk(tenantId, child, ref edited);
                    if (!ReferenceEquals(replaced, child)) array[i] = replaced;
                }

                return array;
            case JsonValue value when value.TryGetValue<string>(out var text) && Resolve(tenantId, text) is { } server:
                edited = true;
                return JsonValue.Create(server);
            default:
                return node;
        }
    }
}
