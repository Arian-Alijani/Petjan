using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Application.Commands;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Sync.Custom;

/// <summary>
/// G05 real command transport: <c>POST /api/commands</c> (single) and <c>POST /api/commands/batch</c> (≤ <see cref="MaxBatchSize"/>) over the
/// shared named <see cref="HttpClient"/>. Every expected HTTP outcome becomes a <see cref="Result"/> — only a caller-initiated cancellation
/// surfaces as <see cref="OperationCanceledException"/>. A reached-intake conversation always answers 200 + <see cref="SettlementResult"/>;
/// anything transient (network fault, timeout, 429/5xx) becomes a <c>Deferred</c> settlement so the command stays pending and the processor
/// retries the <b>same</b> envelope (same <c>OperationId</c> — the server returns the stored result, never a duplicate effect).
/// </summary>
public sealed class HttpCommandTransport(IHttpClientFactory httpFactory, ServerEndpointOptions options, IClock clock)
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

        using var request = NewRequest(CommandsPath, envelope);
        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Response is null) return Result<SettlementResult>.Ok(Deferred(envelope, exchange.Detail));

        using var response = exchange.Response;
        switch (Classify(response))
        {
            case Disposition.Success:
                var body = await ReadAsync<SettlementResult>(response, ct).ConfigureAwait(false);
                if (body is null || !string.Equals(body.OperationId, envelope.OperationId, StringComparison.Ordinal))
                    return Result<SettlementResult>.Fail(ErrorCodes.SyncProtocolViolation, "Settlement response does not answer the submitted operation.");
                return Result<SettlementResult>.Ok(body);
            case Disposition.ContractUnsupported:
                return Result<SettlementResult>.Ok(ContractUnsupported(envelope, response));
            case Disposition.Unauthorized:
                return Result<SettlementResult>.Fail(ErrorCodes.Unauthorized);
            case Disposition.TooLarge:
                return Result<SettlementResult>.Fail(ErrorCodes.BatchTooLarge);
            case Disposition.Invalid:
                return Result<SettlementResult>.Fail(ErrorCodes.ValidationFailed, $"Server rejected the envelope ({(int)response.StatusCode}).");
            case Disposition.Transient:
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
