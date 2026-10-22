using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;

namespace Animora.Sync.Custom;

/// <summary>
/// Typed client over the server's immutable-batch feed (<c>/sync/*</c>). Every expected HTTP status maps to a <see cref="Result"/>;
/// only a caller-initiated cancellation surfaces as <see cref="OperationCanceledException"/>. Pages arrive as raw gzip
/// (<c>application/gzip</c>, not <c>Content-Encoding</c>), so automatic decompression must stay off and the hash is taken over the raw bytes.
/// </summary>
public sealed class SyncFeedClient
{
    /// <summary>Name of the <see cref="HttpClient"/> registered by <c>AddAnimoraHttpSyncEngine</c>.</summary>
    public const string HttpClientName = "animora-sync";

    private const int DecompressionRatioCap = 64;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly ServerEndpointOptions _options;

    /// <summary>Creates a client over <paramref name="http"/> (its BaseAddress must be the server root).</summary>
    public SyncFeedClient(HttpClient http, ServerEndpointOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>GET /sync/manifest.</summary>
    public async Task<Result<SyncManifest>> GetManifestAsync(CancellationToken ct = default)
    {
        using var request = NewRequest(HttpMethod.Get, "sync/manifest");
        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Failure is { } fail) return Result<SyncManifest>.Fail(fail.Error, fail.Detail);
        using var response = exchange.Response!;
        var mapped = MapStatus(response);
        if (mapped is { } err) return Result<SyncManifest>.Fail(err.Error, err.Detail);
        var body = await ReadJsonAsync<SyncManifest>(response, ct).ConfigureAwait(false);
        return body.IsFailure ? Result<SyncManifest>.Fail(body.Error, body.Detail) : Result<SyncManifest>.Ok(body.Value!);
    }

    /// <summary>POST /sync/batches — freezes (or reports on) the delta after <paramref name="fromSequence"/>.</summary>
    public async Task<Result<OpenBatchOutcome>> OpenBatchAsync(long epoch, long fromSequence, CancellationToken ct = default)
    {
        using var request = NewRequest(HttpMethod.Post, "sync/batches");
        request.Content = JsonContent.Create(new OpenBatchRequest(epoch, fromSequence), options: JsonOptions);
        return await OpenAsync(request, epoch, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POST /sync/snapshots — asks the server for a complete, retention-independent full snapshot of the current epoch.
    /// Unlike <see cref="OpenBatchAsync"/> (which hits the retention-enforcing <c>/sync/batches</c> route and answers
    /// <c>ResyncRequired(BEYOND_RETENTION)</c> once the requested sequence has been pruned), this is the dedicated
    /// resync path and always materialises a fresh <c>BatchReady</c> full snapshot for the server's current epoch.
    /// </summary>
    public async Task<Result<OpenBatchOutcome>> OpenSnapshotAsync(CancellationToken ct = default)
    {
        using var request = NewRequest(HttpMethod.Post, "sync/snapshots");
        // The server ignores any body here (it always freezes a full snapshot of the current epoch); send an empty
        // JSON object so the request still carries a Content-Type and is well-formed for proxies/filters.
        request.Content = JsonContent.Create(new { }, options: JsonOptions);
        return await OpenAsync(request, epoch: 0, ct).ConfigureAwait(false);
    }

    /// <summary>Shared transport + response mapping for the two "open a batch/snapshot" POST routes.</summary>
    private async Task<Result<OpenBatchOutcome>> OpenAsync(HttpRequestMessage request, long epoch, CancellationToken ct)
    {
        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Failure is { } fail) return Result<OpenBatchOutcome>.Fail(fail.Error, fail.Detail);
        using var response = exchange.Response!;

        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
            return Result<OpenBatchOutcome>.Ok(OpenBatchOutcome.Resync("CONTRACT_UNSUPPORTED", epoch));
        var mapped = MapStatus(response);
        if (mapped is { } err) return Result<OpenBatchOutcome>.Fail(err.Error, err.Detail);

        var body = await ReadJsonAsync<OpenBatchResponse>(response, ct).ConfigureAwait(false);
        if (body.IsFailure) return Result<OpenBatchOutcome>.Fail(body.Error, body.Detail);
        var value = body.Value!;
        return value.Kind switch
        {
            "BatchReady" when value.Batch is { } batch => Result<OpenBatchOutcome>.Ok(OpenBatchOutcome.Ready(batch)),
            "UpToDate" when value.Current is { } current => Result<OpenBatchOutcome>.Ok(OpenBatchOutcome.Current(current.Watermark)),
            "ResyncRequired" when value.Resync is { } resync => Result<OpenBatchOutcome>.Ok(OpenBatchOutcome.Resync(resync.Reason, resync.Epoch)),
            _ => Result<OpenBatchOutcome>.Fail(ErrorCodes.SyncProtocolViolation, $"Unknown or incomplete open-batch response '{value.Kind}'."),
        };
    }

    /// <summary>
    /// GET /sync/batches/{batchId}/pages/{ordinal}. Returns Ok(null) on 304 (the caller's staged copy is current). On 200 the
    /// raw gzip bytes are hashed and matched against the ETag, then decompressed; the returned page carries the verified hash.
    /// </summary>
    public async Task<Result<SyncPage?>> GetPageAsync(string batchId, int ordinal, string? ifNoneMatch, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(batchId) || ordinal < 0)
            return Result<SyncPage?>.Fail(ErrorCodes.ValidationFailed, "A batch id and a non-negative page ordinal are required.");

        using var request = NewRequest(HttpMethod.Get, $"sync/batches/{Uri.EscapeDataString(batchId)}/pages/{ordinal.ToString(CultureInfo.InvariantCulture)}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/gzip"));
        if (!string.IsNullOrEmpty(ifNoneMatch))
            request.Headers.TryAddWithoutValidation("If-None-Match", "\"" + ifNoneMatch.Trim('"') + "\"");

        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Failure is { } fail) return Result<SyncPage?>.Fail(fail.Error, fail.Detail);
        using var response = exchange.Response!;

        if (response.StatusCode == HttpStatusCode.NotModified)
            return Result<SyncPage?>.Ok(null);
        var mapped = MapStatus(response);
        if (mapped is { } err) return Result<SyncPage?>.Fail(err.Error, err.Detail);

        var etag = response.Headers.ETag?.Tag?.Trim('"');
        if (string.IsNullOrEmpty(etag))
            return Result<SyncPage?>.Fail(ErrorCodes.SyncProtocolViolation, "Page response carries no ETag.");

        var raw = await ReadBoundedAsync(response, _options.MaxPageBytes, ct).ConfigureAwait(false);
        if (raw.IsFailure) return Result<SyncPage?>.Fail(raw.Error, raw.Detail);
        var bytes = raw.Value!;

        var hash = PageIntegrity.ComputePageHash(bytes);
        if (!string.Equals(hash, etag, StringComparison.OrdinalIgnoreCase))
            return Result<SyncPage?>.Fail(ErrorCodes.SyncIntegrityFailed, $"Page {ordinal} hash does not match its ETag.");

        SyncPage? page;
        try
        {
            using var gzip = new GZipStream(new MemoryStream(bytes, writable: false), CompressionMode.Decompress);
            using var json = new MemoryStream();
            var cap = (long)_options.MaxPageBytes * DecompressionRatioCap;
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await gzip.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (json.Length + read > cap)
                    return Result<SyncPage?>.Fail(ErrorCodes.SyncProtocolViolation, $"Page {ordinal} expands beyond the allowed size.");
                json.Write(buffer, 0, read);
            }

            json.Position = 0;
            page = await JsonSerializer.DeserializeAsync<SyncPage>(json, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            return Result<SyncPage?>.Fail(ErrorCodes.SyncProtocolViolation, $"Page {ordinal} is not valid gzip JSON.");
        }

        if (page is null || page.Records is null || page.BatchId != batchId || page.PageOrdinal != ordinal || page.PageCount < 1)
            return Result<SyncPage?>.Fail(ErrorCodes.SyncProtocolViolation, $"Page {ordinal} has an invalid structure.");

        return Result<SyncPage?>.Ok(page with { PageHash = hash });
    }

    /// <summary>
    /// D04 — GET /finance/ledger?since={ticks}&amp;take={n}: the server-authoritative financial read state (invoices/payments) that
    /// changed at or after a UTC-tick cursor. A separate read-only channel from the state feed and the command intake.
    /// </summary>
    public async Task<Result<FinanceReadPage>> GetFinanceLedgerAsync(long sinceTicks, int take, CancellationToken ct = default)
    {
        var path = $"finance/ledger?since={Math.Max(0, sinceTicks).ToString(CultureInfo.InvariantCulture)}&take={Math.Max(1, take).ToString(CultureInfo.InvariantCulture)}";
        using var request = NewRequest(HttpMethod.Get, path);
        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Failure is { } fail) return Result<FinanceReadPage>.Fail(fail.Error, fail.Detail);
        using var response = exchange.Response!;
        var mapped = MapStatus(response);
        if (mapped is { } err) return Result<FinanceReadPage>.Fail(err.Error, err.Detail);
        var body = await ReadJsonAsync<FinanceReadPage>(response, ct).ConfigureAwait(false);
        if (body.IsFailure) return Result<FinanceReadPage>.Fail(body.Error, body.Detail);
        var page = body.Value!;
        return page.Invoices is null || page.Payments is null
            ? Result<FinanceReadPage>.Fail(ErrorCodes.SyncProtocolViolation, "Finance ledger page is missing its collections.")
            : Result<FinanceReadPage>.Ok(page);
    }

    /// <summary>
    /// GET /notifications?since={ticks}&amp;take={n}: the vendor-authored messages addressed to this device's signed-in
    /// user, created after a UTC-tick cursor. A separate read-only channel from the state feed and the command intake.
    /// </summary>
    public async Task<Result<NotificationFeedPage>> GetNotificationFeedAsync(long sinceTicks, int take, CancellationToken ct = default)
    {
        var path = $"notifications?since={Math.Max(0, sinceTicks).ToString(CultureInfo.InvariantCulture)}&take={Math.Max(1, take).ToString(CultureInfo.InvariantCulture)}";
        using var request = NewRequest(HttpMethod.Get, path);
        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Failure is { } fail) return Result<NotificationFeedPage>.Fail(fail.Error, fail.Detail);
        using var response = exchange.Response!;
        var mapped = MapStatus(response);
        if (mapped is { } err) return Result<NotificationFeedPage>.Fail(err.Error, err.Detail);
        var body = await ReadJsonAsync<NotificationFeedPage>(response, ct).ConfigureAwait(false);
        if (body.IsFailure) return Result<NotificationFeedPage>.Fail(body.Error, body.Detail);
        var page = body.Value!;
        return page.Items is null
            ? Result<NotificationFeedPage>.Fail(ErrorCodes.SyncProtocolViolation, "Notification feed page is missing its items.")
            : Result<NotificationFeedPage>.Ok(page);
    }

    /// <summary>
    /// POST /notifications/receipts: tells the server which messages this device has actually stored locally, so the
    /// admin panel reports REAL delivery. Idempotent server-side; an empty list is a no-op that sends nothing.
    /// </summary>
    public async Task<Result> ReportNotificationReceiptsAsync(IReadOnlyList<string> notificationIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notificationIds);
        if (notificationIds.Count == 0) return Result.Ok();
        using var request = NewRequest(HttpMethod.Post, "notifications/receipts");
        request.Content = JsonContent.Create(new NotificationReceiptReport(notificationIds), options: JsonOptions);
        var exchange = await SendAsync(request, ct).ConfigureAwait(false);
        if (exchange.Failure is { } fail) return Result.Fail(fail.Error, fail.Detail);
        using var response = exchange.Response!;
        var mapped = MapStatus(response);
        return mapped is { } err ? Result.Fail(err.Error, err.Detail) : Result.Ok();
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.TryAddWithoutValidation(WireContract.HeaderName, WireContract.Version.ToString(CultureInfo.InvariantCulture));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private readonly record struct Exchange(HttpResponseMessage? Response, (ErrorCode Error, string? Detail)? Failure);

    private async Task<Exchange> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return new Exchange(response, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Exchange(null, (ErrorCodes.NetworkUnavailable, "The sync server did not answer in time."));
        }
        catch (HttpRequestException ex)
        {
            return new Exchange(null, (ErrorCodes.NetworkUnavailable, ex.Message));
        }
    }

    /// <summary>Maps the non-success statuses shared by every call; null means the status is success (2xx).</summary>
    private static (ErrorCode Error, string? Detail)? MapStatus(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return null;
        return (int)response.StatusCode switch
        {
            401 or 403 => (ErrorCodes.Unauthorized, null),
            404 => (ErrorCodes.BatchNotFound, null),
            426 => (ErrorCodes.ResyncRequired, "CONTRACT_UNSUPPORTED"),
            429 or 503 => (ErrorCodes.ServerBusy, RetryAfterSeconds(response)),
            >= 500 => (ErrorCodes.NetworkUnavailable, $"Server error {(int)response.StatusCode}."),
            _ => (ErrorCodes.SyncProtocolViolation, $"Unexpected status {(int)response.StatusCode}."),
        };
    }

    private static string? RetryAfterSeconds(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry is null) return null;
        if (retry.Delta is { } delta) return ((long)Math.Ceiling(delta.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        if (retry.Date is { } date)
            return ((long)Math.Max(0, Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds))).ToString(CultureInfo.InvariantCulture);
        return null;
    }

    private static async Task<Result<T>> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
            return value is null
                ? Result<T>.Fail(ErrorCodes.SyncProtocolViolation, "Empty response body.")
                : Result<T>.Ok(value);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return Result<T>.Fail(ErrorCodes.SyncProtocolViolation, "Response body is not valid JSON.");
        }
        catch (HttpRequestException ex)
        {
            return Result<T>.Fail(ErrorCodes.NetworkUnavailable, ex.Message);
        }
    }

    private async Task<Result<byte[]>> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
            return Result<byte[]>.Fail(ErrorCodes.SyncProtocolViolation, $"Page body ({declared} bytes) exceeds {maxBytes}.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    return Result<byte[]>.Fail(ErrorCodes.SyncProtocolViolation, $"Page body exceeds {maxBytes} bytes.");
                buffer.Write(chunk, 0, read);
            }

            return Result<byte[]>.Ok(buffer.ToArray());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<byte[]>.Fail(ErrorCodes.NetworkUnavailable, "The sync server stalled while sending a page.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Result<byte[]>.Fail(ErrorCodes.NetworkUnavailable, ex.Message);
        }
    }
}
