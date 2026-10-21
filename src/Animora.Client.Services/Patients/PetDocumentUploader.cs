using System.Net;
using System.Text.Json;
using Animora.Client.Services.Identity;
using Animora.Domain.Results;

namespace Animora.Client.Services.Patients;

/// <summary>
/// Uploads one patient-file attachment to the server's chunked, content-addressed file store and returns the
/// server file id, so the bytes exist server-side and can be re-fetched after a store rebuild or on another
/// device that holds the reference.
/// </summary>
/// <remarks>
/// The protocol is the existing one under <c>/files</c> — declare, PUT 512 KiB chunks at an offset, complete — which
/// gives resumability, a short-lived valet key per upload, a per-instance concurrency gate, and a server-side
/// re-check of the size, the SHA-256, the content type and the magic bytes at <c>complete</c>. Nothing here re-implements
/// those guarantees; it only drives them. Network faults surface as <see cref="ErrorCodes.NetworkUnavailable"/> and
/// never leave a half-recorded document behind, because the caller only writes the reference after this succeeds.
/// </remarks>
internal sealed class PetDocumentUploader(OnlineApi api)
{
    /// <summary>One PUT body. Half the server's 1 MiB ceiling keeps the per-request buffer modest on clinic hardware.</summary>
    private const int ChunkBytes = 512 * 1024;

    private const string FilesRoot = "/files";
    private const string ValetHeader = "X-Animora-Valet";
    private const string NextOffsetHeader = "X-Animora-Next-Offset";

    /// <summary>Declares, transfers and completes one payload. Returns the server file id.</summary>
    public async Task<Result<string>> UploadAsync(string fileName, string contentType, ReadOnlyMemory<byte> bytes,
        string sha256, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (bytes.IsEmpty) return Result<string>.Fail(ErrorCodes.ValidationFailed);

        // 1) Declare: the server reserves the upload, validates type/size/extension and hands back a valet key.
        var declare = await api.SendAsync(HttpMethod.Post, FilesRoot,
            new { name = fileName, contentType, size = bytes.Length, sha256 }, bearer: null, ct).ConfigureAwait(false);
        using var declaredResponse = declare.Response;
        if (declaredResponse is null) return Result<string>.Fail(ErrorCodes.NetworkUnavailable, declare.Detail);
        if (!declaredResponse.IsSuccessStatusCode) return Result<string>.Fail(await MapAsync(declaredResponse, ct).ConfigureAwait(false));
        var declared = await OnlineApi.ReadAsync<DeclaredFile>(declaredResponse, ct).ConfigureAwait(false);
        if (declared?.UploadId is not { Length: > 0 } uploadId || declared.Valet is not { Length: > 0 } valet)
            return Result<string>.Fail(ErrorCodes.NetworkUnavailable, "The server returned an unusable upload handle.");

        // 2) Chunks. The server answers with the authoritative next offset, so a short write resumes correctly
        //    instead of silently shifting the rest of the file.
        long offset = 0;
        while (offset < bytes.Length)
        {
            int take = (int)Math.Min(ChunkBytes, bytes.Length - offset);
            var chunk = await api.SendBytesAsync(HttpMethod.Put,
                $"{FilesRoot}/uploads/{uploadId}?offset={offset}", bytes.Slice((int)offset, take),
                new Dictionary<string, string>(StringComparer.Ordinal) { [ValetHeader] = valet }, ct).ConfigureAwait(false);
            using var chunkResponse = chunk.Response;
            if (chunkResponse is null) return Result<string>.Fail(ErrorCodes.NetworkUnavailable, chunk.Detail);
            if (!chunkResponse.IsSuccessStatusCode) return Result<string>.Fail(await MapAsync(chunkResponse, ct).ConfigureAwait(false));
            offset = NextOffset(chunkResponse) ?? offset + take;
        }

        // 3) Complete: the server re-hashes the staged payload, re-sniffs the magic bytes and publishes the blob.
        var complete = await api.SendAsync(HttpMethod.Post, $"{FilesRoot}/uploads/{uploadId}/complete", null, null, ct,
            new Dictionary<string, string>(StringComparer.Ordinal) { [ValetHeader] = valet }).ConfigureAwait(false);
        using var completedResponse = complete.Response;
        if (completedResponse is null) return Result<string>.Fail(ErrorCodes.NetworkUnavailable, complete.Detail);
        if (!completedResponse.IsSuccessStatusCode) return Result<string>.Fail(await MapAsync(completedResponse, ct).ConfigureAwait(false));
        var stored = await OnlineApi.ReadAsync<CompletedFile>(completedResponse, ct).ConfigureAwait(false);
        return stored?.FileId is { Length: > 0 } fileId
            ? Result<string>.Ok(fileId)
            : Result<string>.Fail(ErrorCodes.NetworkUnavailable, "The server did not confirm the stored file.");
    }

    /// <summary>Downloads a committed payload by its server file id (used when the local copy is gone).</summary>
    public async Task<Result<Stream>> DownloadAsync(string fileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var valet = await api.SendAsync(HttpMethod.Post, $"{FilesRoot}/{fileId}/valet",
            new { operation = "download" }, bearer: null, ct).ConfigureAwait(false);
        using var valetResponse = valet.Response;
        if (valetResponse is null) return Result<Stream>.Fail(ErrorCodes.NetworkUnavailable, valet.Detail);
        if (!valetResponse.IsSuccessStatusCode) return Result<Stream>.Fail(await MapAsync(valetResponse, ct).ConfigureAwait(false));
        var granted = await OnlineApi.ReadAsync<ValetFile>(valetResponse, ct).ConfigureAwait(false);
        if (granted?.Valet is not { Length: > 0 } key) return Result<Stream>.Fail(ErrorCodes.NetworkUnavailable);

        var exchange = await api.SendAsync(HttpMethod.Get, $"{FilesRoot}/{fileId}", null, null, ct,
            new Dictionary<string, string>(StringComparer.Ordinal) { [ValetHeader] = key }).ConfigureAwait(false);
        if (exchange.Response is null) return Result<Stream>.Fail(ErrorCodes.NetworkUnavailable, exchange.Detail);
        if (!exchange.Response.IsSuccessStatusCode)
        {
            var error = await MapAsync(exchange.Response, ct).ConfigureAwait(false);
            exchange.Response.Dispose();
            return Result<Stream>.Fail(error);
        }

        // The caller owns the stream (and with it the response), so it is not disposed here.
        var buffer = await exchange.Response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return Result<Stream>.Ok(buffer);
    }

    private static long? NextOffset(HttpResponseMessage response) =>
        response.Headers.TryGetValues(NextOffsetHeader, out var values) &&
        long.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var next) && next >= 0
            ? next
            : null;

    /// <summary>Maps a failed response to the one code the UI can explain; the server's own code wins when it is known.</summary>
    private static async Task<ErrorCode> MapAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var wire = await OnlineApi.ReadAsync<WireFailure>(response, ct).ConfigureAwait(false);
            if (wire?.Code is { Length: > 0 } code && Known.TryGetValue(code, out var known))
            {
                return known;
            }
        }
        catch (JsonException)
        {
            // A non-JSON body (proxy error page) falls through to the status mapping.
        }

        return response.StatusCode switch
        {
            HttpStatusCode.RequestEntityTooLarge => ErrorCodes.FileTooLarge,
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => ErrorCodes.PermissionDenied,
            HttpStatusCode.Conflict => ErrorCodes.ConcurrencyConflict,
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => ErrorCodes.StoreUnavailable,
            HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType => ErrorCodes.ValidationFailed,
            _ => ErrorCodes.NetworkUnavailable,
        };
    }

    /// <summary>The wire codes the file endpoints can return, mapped to the domain code the UI understands.</summary>
    private static readonly Dictionary<string, ErrorCode> Known = new(StringComparer.Ordinal)
    {
        [ErrorCodes.FileTypeNotAllowed.Code] = ErrorCodes.FileTypeNotAllowed,
        [ErrorCodes.FileTooLarge.Code] = ErrorCodes.FileTooLarge,
        [ErrorCodes.FileHashMismatch.Code] = ErrorCodes.FileHashMismatch,
        [ErrorCodes.FileNotFound.Code] = ErrorCodes.FileNotFound,
        [ErrorCodes.ValetKeyInvalid.Code] = ErrorCodes.ValetKeyInvalid,
        [ErrorCodes.ServerBusy.Code] = ErrorCodes.ServerBusy,
        [ErrorCodes.ValidationFailed.Code] = ErrorCodes.ValidationFailed,
        [ErrorCodes.ConcurrencyConflict.Code] = ErrorCodes.ConcurrencyConflict,
        [ErrorCodes.PermissionDenied.Code] = ErrorCodes.PermissionDenied,
    };

    private sealed record DeclaredFile(string? FileId, string? UploadId, string? Valet);

    private sealed record CompletedFile(string? FileId);

    private sealed record ValetFile(string? Valet);

    private sealed record WireFailure(string? Code);
}
