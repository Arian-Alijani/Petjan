using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Sync.Custom;

/// <summary>
/// G05 real command transport: <c>POST /api/commands</c> (single) and <c>POST /api/commands/batch</c> (≤ <see cref="MaxBatchSize"/>) over the
/// shared named <see cref="HttpClient"/>. Every expected HTTP outcome becomes a <see cref="Result"/> — only a caller-initiated cancellation
/// surfaces as <see cref="OperationCanceledException"/>. A reached-intake conversation always answers 200 + <see cref="SettlementResult"/>;
/// anything transient (network fault, timeout, 429/5xx) becomes a <c>Deferred</c> settlement so the command stays pending and the processor
/// retries the <b>same</b> envelope (same <c>OperationId</c> — the server returns the stored result, never a duplicate effect).
/// <para>
/// <b>Identity rebinding (local → server).</b> The local store is a single-tenant replica stamped with the installation tenant
/// (<c>Tenant.LocalDefaultId</c> / <c>"T-LOCAL-DEFAULT"</c>) and local user/device ids until the installation is bound online, so a queued
/// envelope carries those placeholders — which the server rejects as non-ULID (<c>400 VALIDATION_FAILED "malformed envelope"</c>, the first
/// step of a manual sync, surfacing as <c>SyncState.Error</c>). Because the identity fields are <b>not</b> part of the command fingerprint
/// (fingerprint = command type + payload only) and the server re-resolves the tenant from the bearer-token membership
/// (<see cref="CommandEnvelope.TenantId"/> is "ignored server-side and re-resolved from membership", A-RULE 012), the authoritative server
/// <c>TenantId</c>/<c>UserId</c>/<c>DeviceId</c> from the stored online session are stamped onto each outgoing envelope here, at the
/// local↔wire boundary. <c>OperationId</c>, <c>CommandFingerprint</c> and <c>PayloadJson</c> are never touched, so idempotent retries stay
/// byte-identical. With no session store wired or no active session the envelope is sent unchanged (offline / unit composition).
/// </para>
/// <para>
/// <b>Author guard (D18).</b> Rebinding is refused when the queued command was authored by a DIFFERENT user than the
/// active session (same tenant, one local database, e.g. A queues work, signs out, B signs in). Submitting it would
/// make the server attribute and authorize A's work as B, so the command is held (<c>IDENTITY_MISMATCH</c>) until its
/// original author signs in again — see <see cref="CanSubmitAs"/>.
/// </para>
/// </summary>
public sealed class HttpCommandTransport(IHttpClientFactory httpFactory, ServerEndpointOptions options, IClock clock,
    ISessionStore? sessions = null)
    : ICommandTransport, ICommandBatchTransport
{
    /// <summary>The server's batch ceiling; a larger batch is refused client-side without a request.</summary>
    public const int MaxBatchSize = 50;

    private const string CommandsPath = "api/commands";
    private const string BatchPath = "api/commands/batch";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        TraceLog.Info($"CommandTransport.Submit → op={envelope.OperationId}, type={envelope.CommandType}, aggregate={envelope.AggregateType}/{envelope.AggregateId}, localSequence={envelope.LocalSequence}.", "sync.push");
        var session = await SessionAsync(ct).ConfigureAwait(false);
        if (session is not null && !CanSubmitAs(envelope, session))
        {
            TraceLog.Warn($"CommandTransport.Submit ← op={envelope.OperationId} HELD: queued by user {envelope.UserId} but the active session is {session.UserId} (same tenant) — refusing to submit another user's work as the current actor.", "sync.push");
            return Result<SettlementResult>.Fail(ErrorCodes.IdentityMismatch,
                $"Operation {envelope.OperationId} was queued by a different user; it is held until that user signs in again.");
        }

        envelope = Rebind(envelope, session);
        using var request = NewRequest(CommandsPath, envelope);
        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Response is null)
        {
            TraceLog.Warn($"CommandTransport.Submit ← op={envelope.OperationId} DEFERRED (transport): {exchange.Detail}", "sync.push");
            return Result<SettlementResult>.Ok(Deferred(envelope, exchange.Detail));
        }

        using var response = exchange.Response;
        switch (Classify(response))
        {
            case Disposition.Success:
                var body = await ReadAsync<SettlementResult>(response, ct).ConfigureAwait(false);
                if (body is null || !string.Equals(body.OperationId, envelope.OperationId, StringComparison.Ordinal))
                    return Result<SettlementResult>.Fail(ErrorCodes.SyncProtocolViolation, "Settlement response does not answer the submitted operation.");
                TraceLog.Info($"CommandTransport.Submit ← op={envelope.OperationId} HTTP {(int)response.StatusCode} SETTLED outcome={body.Outcome}, serverEntityId={body.ServerEntityId}, serverVersion={body.ServerVersion}, error={body.ErrorCode ?? "(none)"}.", "sync.push");
                return Result<SettlementResult>.Ok(body);
            case Disposition.ContractUnsupported:
                return Result<SettlementResult>.Ok(ContractUnsupported(envelope, response));
            case Disposition.Unauthorized:
                TraceLog.Warn($"CommandTransport.Submit ← op={envelope.OperationId} UNAUTHORIZED (401/403).", "sync.push");
                return Result<SettlementResult>.Fail(ErrorCodes.Unauthorized);
            case Disposition.TooLarge:
                return Result<SettlementResult>.Fail(ErrorCodes.BatchTooLarge);
            case Disposition.Invalid:
                TraceLog.Warn($"CommandTransport.Submit ← op={envelope.OperationId} INVALID ({(int)response.StatusCode}) → VALIDATION_FAILED.", "sync.push");
                return Result<SettlementResult>.Fail(ErrorCodes.ValidationFailed, $"Server rejected the envelope ({(int)response.StatusCode}).");
            case Disposition.Transient:
                TraceLog.Warn($"CommandTransport.Submit ← op={envelope.OperationId} TRANSIENT ({(int)response.StatusCode}) → Deferred, will retry same OperationId.", "sync.push");
                return Result<SettlementResult>.Ok(Deferred(envelope, $"Server answered {(int)response.StatusCode}; will retry."));
            default:
                return Result<SettlementResult>.Fail(ErrorCodes.SyncProtocolViolation, $"Unexpected status {(int)response.StatusCode}.");
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SettlementResult>>> SubmitBatchAsync(IReadOnlyList<CommandEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        if (envelopes.Count == 0) return Result<IReadOnlyList<SettlementResult>>.Ok([]);
        if (envelopes.Count > MaxBatchSize)
            return Result<IReadOnlyList<SettlementResult>>.Fail(ErrorCodes.BatchTooLarge, $"{envelopes.Count} > {MaxBatchSize}");

        // D18: a batch is refused as a whole when any member was queued by another user of this tenant — the
        // response is positional, and submitting a foreign envelope under the current actor would be exactly the
        // misattribution this guard exists to prevent. Nothing is sent; the eligible commands go out on a later pass.
        var session = await SessionAsync(ct).ConfigureAwait(false);
        if (session is not null && envelopes.Any(e => !CanSubmitAs(e, session)))
        {
            TraceLog.Warn($"CommandTransport.SubmitBatch ← {envelopes.Count} envelope(s) HELD: at least one was queued by a different user than the active session {session.UserId} (same tenant).", "sync.push");
            return Result<IReadOnlyList<SettlementResult>>.Fail(ErrorCodes.IdentityMismatch,
                "At least one queued command belongs to a different user; the batch is held until that user signs in again.");
        }

        envelopes = RebindAll(envelopes, session);
        using var request = NewRequest(BatchPath, envelopes);
        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Response is null) return Result<IReadOnlyList<SettlementResult>>.Ok(DeferAll(envelopes, exchange.Detail));

        using var response = exchange.Response;
        switch (Classify(response))
        {
            case Disposition.Success:
                var body = await ReadAsync<SettlementResult[]>(response, ct).ConfigureAwait(false);
                if (body is null || body.Length != envelopes.Count)
                    return Result<IReadOnlyList<SettlementResult>>.Fail(ErrorCodes.SyncProtocolViolation, "Batch response size does not match the request.");
                for (var i = 0; i < body.Length; i++)
                {
                    // Results are positional (server processes strictly in request order); a mismatch is a protocol violation, not data.
                    if (!string.Equals(body[i]?.OperationId, envelopes[i].OperationId, StringComparison.Ordinal))
                        return Result<IReadOnlyList<SettlementResult>>.Fail(ErrorCodes.SyncProtocolViolation, "Batch response is not in request order.");
                }

                return Result<IReadOnlyList<SettlementResult>>.Ok(body);
            case Disposition.ContractUnsupported:
                return Result<IReadOnlyList<SettlementResult>>.Ok(envelopes.Select(e => ContractUnsupported(e, response)).ToArray());
            case Disposition.Unauthorized:
                return Result<IReadOnlyList<SettlementResult>>.Fail(ErrorCodes.Unauthorized);
            case Disposition.TooLarge:
                return Result<IReadOnlyList<SettlementResult>>.Fail(ErrorCodes.BatchTooLarge);
            case Disposition.Invalid:
                return Result<IReadOnlyList<SettlementResult>>.Fail(ErrorCodes.ValidationFailed, $"Server rejected the batch ({(int)response.StatusCode}).");
            case Disposition.Transient:
                return Result<IReadOnlyList<SettlementResult>>.Ok(DeferAll(envelopes, $"Server answered {(int)response.StatusCode}; will retry."));
            default:
                return Result<IReadOnlyList<SettlementResult>>.Fail(ErrorCodes.SyncProtocolViolation, $"Unexpected status {(int)response.StatusCode}.");
        }
    }

    private enum Disposition { Success, ContractUnsupported, Unauthorized, TooLarge, Invalid, Transient, Unexpected }

    private static Disposition Classify(HttpResponseMessage response) => (int)response.StatusCode switch
    {
        200 => Disposition.Success,
        426 => Disposition.ContractUnsupported,
        401 or 403 => Disposition.Unauthorized,
        413 => Disposition.TooLarge,
        400 => Disposition.Invalid,
        408 or 429 or >= 500 => Disposition.Transient,
        _ => Disposition.Unexpected,
    };

    /// <summary>
    /// The stored online session, or <see langword="null"/> when no session store is wired or nothing is stored —
    /// in which case envelopes are sent unchanged (offline / unit composition).
    /// </summary>
    private async Task<ClientSession?> SessionAsync(CancellationToken ct)
    {
        if (sessions is null) return null;
        var read = await sessions.ReadAsync(ct).ConfigureAwait(false);
        return read.IsFailure ? null : read.Value;
    }

    /// <summary>
    /// D18: a queued command may only be submitted when its ORIGINATING actor is the current session's user.
    /// <para>
    /// Rebinding stamps the session identity onto the envelope, and the server re-resolves the tenant from the bearer
    /// membership and attributes the work to that session. Stamping it onto a command that another user queued on
    /// the same device (A queues work, signs out, B signs in — same tenant, one local database) therefore sent A's
    /// work as B, and the server authorized and recorded it as B. Only two things may be rebound:
    /// </para>
    /// <list type="bullet">
    /// <item><description>an envelope authored by the current session user (a normal retry);</description></item>
    /// <item><description>a PRE-BINDING envelope — one still carrying the local installation's placeholder tenant
    /// (<see cref="Tenant.LocalDefaultId"/>), which is precisely what rebinding exists to upgrade.</description></item>
    /// </list>
    /// <para>Anything else is held for its original author instead of being submitted under the wrong actor.</para>
    /// </summary>
    private static bool CanSubmitAs(CommandEnvelope envelope, ClientSession session) =>
        IsPreBinding(envelope.TenantId)
        || string.Equals(envelope.UserId, session.UserId, StringComparison.Ordinal);

    /// <summary>True while the envelope still carries the local installation's placeholder identity.</summary>
    private static bool IsPreBinding(string? tenantId) =>
        string.IsNullOrEmpty(tenantId) || string.Equals(tenantId, Tenant.LocalDefaultId, StringComparison.Ordinal);

    private static IReadOnlyList<CommandEnvelope> RebindAll(IReadOnlyList<CommandEnvelope> envelopes, ClientSession? session)
    {
        if (session is null) return envelopes;
        var rebound = new CommandEnvelope[envelopes.Count];
        for (var i = 0; i < envelopes.Count; i++) rebound[i] = Rebind(envelopes[i], session);
        return rebound;
    }

    /// <summary>
    /// Returns <paramref name="envelope"/> re-stamped with the session's server identity, or the original when the session identity is
    /// incomplete or already matches. <c>OperationId</c>/<c>CommandFingerprint</c>/<c>PayloadJson</c> are preserved by the <c>with</c> copy.
    /// The caller has already established that the envelope may be submitted as this session (see <see cref="CanSubmitAs"/>).
    /// </summary>
    private static CommandEnvelope Rebind(CommandEnvelope envelope, ClientSession? session)
    {
        if (session is null) return envelope;
        if (string.IsNullOrEmpty(session.TenantId) || string.IsNullOrEmpty(session.UserId) || string.IsNullOrEmpty(session.DeviceId))
            return envelope;
        if (string.Equals(envelope.TenantId, session.TenantId, StringComparison.Ordinal)
            && string.Equals(envelope.UserId, session.UserId, StringComparison.Ordinal)
            && string.Equals(envelope.DeviceId, session.DeviceId, StringComparison.Ordinal))
            return envelope;
        return envelope with { TenantId = session.TenantId, UserId = session.UserId, DeviceId = session.DeviceId };
    }

    private HttpRequestMessage NewRequest<T>(string path, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonOptions) };
        request.Headers.TryAddWithoutValidation(WireContract.HeaderName, WireContract.Version.ToString(CultureInfo.InvariantCulture));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private readonly record struct Exchange(HttpResponseMessage? Response, string? Detail);

    private async Task<Exchange> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.RequestTimeout);
        try
        {
            var http = httpFactory.CreateClient(SyncFeedClient.HttpClientName);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return new Exchange(response, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Exchange(null, "The command server did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new Exchange(null, ex.Message);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private SettlementResult Deferred(CommandEnvelope envelope, string? detail) => new(
        envelope.OperationId, SettlementOutcome.Deferred, ErrorCode: null, detail, ServerEntityId: null, ServerVersion: null,
        clock.UtcNow, FinancialEffect.Unknown, ResponsibleRole: null);

    private IReadOnlyList<SettlementResult> DeferAll(IReadOnlyList<CommandEnvelope> envelopes, string? detail) =>
        envelopes.Select(e => Deferred(e, detail)).ToArray();

    /// <summary>426 is never dropped: the command stays pending (Deferred) carrying the unsupported-contract code until the client is updated.</summary>
    private SettlementResult ContractUnsupported(CommandEnvelope envelope, HttpResponseMessage response)
    {
        var supported = response.Headers.TryGetValues(WireContract.SupportedVersionsHeaderName, out var values) ? string.Join(",", values) : null;
        return new SettlementResult(
            envelope.OperationId, SettlementOutcome.Deferred, ErrorCodes.ContractVersionUnsupported.Code,
            supported is null ? "Server does not support this contract version." : $"Server supports contract versions: {supported}.",
            ServerEntityId: null, ServerVersion: null, clock.UtcNow, FinancialEffect.Unknown, ResponsibleRole: null);
    }
}
