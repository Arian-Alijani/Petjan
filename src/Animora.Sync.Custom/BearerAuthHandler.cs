using System.Net;
using System.Net.Http.Headers;
using Animora.Application.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Sync.Custom;

/// <summary>
/// G06 delegating handler for the shared named <see cref="HttpClient"/>: attaches <c>Authorization: Bearer &lt;access&gt;</c> from the
/// <see cref="ISessionStore"/> and, on a 401 from a protected route, performs exactly ONE single-flight refresh
/// (<see cref="ISessionRefresher"/>) and retries the request once with the rotated token. A second 401 — or a refresh that fails
/// (<c>SESSION_EXPIRED</c>) — surfaces the 401 to the caller (which maps it to the typed <c>UNAUTHORIZED</c> result; the sync session ends
/// cleanly with its checkpoint untouched); there is never a refresh loop. Request bodies above <see cref="MaxReplayBufferBytes"/> are
/// streamed un-buffered (the token is still rotated on a 401 for the caller's idempotent re-submit).
/// Login / refresh / key-set routes are anonymous and never touched. A request that already carries an Authorization header is left alone.
/// </summary>
public sealed class BearerAuthHandler(ISessionStore sessions, IServiceProvider services) : DelegatingHandler
{
    private static readonly string[] AnonymousPaths = ["auth/login", "auth/refresh", "auth/keys", "licensing/keys"];

    // One refresh at a time across every request pipeline: concurrent 401s share the first refresh's outcome.
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsAnonymous(request.RequestUri) || request.Headers.Authorization is not null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var session = await ReadSessionAsync(cancellationToken).ConfigureAwait(false);
        var usedToken = session?.AccessToken;

        // Only small bodies are buffered (once, below the LOH threshold) so the single 401 retry can replay them. A larger body
        // is streamed straight through — no per-request full-body copy — and, if it is answered 401, the token is still rotated
        // so the caller's own idempotent re-submit (same OperationId / immutable page) succeeds with the fresh token.
        var body = await TryBufferAsync(request.Content, cancellationToken).ConfigureAwait(false);
        var replayable = request.Content is null || body is not null;

        if (!replayable)
        {
            if (!string.IsNullOrEmpty(usedToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", usedToken);
            var streamed = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (streamed.StatusCode != HttpStatusCode.Unauthorized || usedToken is null) return streamed;

            // Rotate for the next attempt; this request's body is gone, so the 401 is surfaced (typed UNAUTHORIZED by the transports).
            await RefreshOnceAsync(usedToken, cancellationToken).ConfigureAwait(false);
            return streamed;
        }

        using var first = Clone(request, body, usedToken);
        var response = await base.SendAsync(first, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || usedToken is null) return response;

        var refreshed = await RefreshOnceAsync(usedToken, cancellationToken).ConfigureAwait(false);
        if (refreshed is null) return response; // refresh failed / session gone: surface the original 401 (never a loop)

        response.Dispose();
        using var retry = Clone(request, body, refreshed);
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false); // a second 401 goes straight to the caller
    }

    /// <summary>Largest request body buffered for the single 401 replay; kept under the 85 000-byte LOH threshold.</summary>
    internal const int MaxReplayBufferBytes = 64 * 1024;

    // Returns the body bytes when it is within MaxReplayBufferBytes; null when there is no content OR it is too large to buffer
    // (callers distinguish the two through request.Content). All client contents are re-serialisable (JsonContent/ByteArrayContent). A known-oversized length is never read; an unknown-length body is
    // read through a capped sink that gives up as soon as the cap is exceeded.
    private static async Task<byte[]?> TryBufferAsync(HttpContent? content, CancellationToken ct)
    {
        if (content is null) return null;

        var length = content.Headers.ContentLength;
        if (length is > MaxReplayBufferBytes) return null;
        if (length is not null) return await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        using var sink = new CappedMemoryStream(MaxReplayBufferBytes);
        try
        {
            await content.CopyToAsync(sink, ct).ConfigureAwait(false);
        }
        catch (CappedMemoryStream.CapExceededException)
        {
            return null; // too large: the (re-serialisable) original content is sent as-is
        }

        return sink.ToArray();
    }

    private sealed class CappedMemoryStream(int cap) : MemoryStream
    {
        // Not an IOException: HttpContent wraps IOExceptions raised while serialising, which would hide the cap signal.
        public sealed class CapExceededException : InvalidOperationException;

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > cap) throw new CapExceededException();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > cap) throw new CapExceededException();
            base.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
    }

    private async Task<string?> RefreshOnceAsync(string usedToken, CancellationToken ct)
    {
        await RefreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another request may already have rotated the token while this one waited: just reuse it.
            var current = await ReadSessionAsync(ct).ConfigureAwait(false);
            if (current is null) return null;
            if (!string.Equals(current.AccessToken, usedToken, StringComparison.Ordinal)) return current.AccessToken;

            var refresher = services.GetService<ISessionRefresher>();
            if (refresher is null) return null;
            var result = await refresher.RefreshAsync(ct).ConfigureAwait(false);
            if (result.IsFailure) return null;
            return (await ReadSessionAsync(ct).ConfigureAwait(false))?.AccessToken;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    private async Task<ClientSession?> ReadSessionAsync(CancellationToken ct)
    {
        var read = await sessions.ReadAsync(ct).ConfigureAwait(false);
        return read.IsSuccess ? read.Value : null;
    }

    private static bool IsAnonymous(Uri? uri)
    {
        if (uri is null) return false;
        var path = uri.AbsolutePath.TrimStart('/');
        foreach (var anonymous in AnonymousPaths)
            if (path.EndsWith(anonymous, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body, string? bearer)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version, VersionPolicy = source.VersionPolicy };
        foreach (var header in source.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in source.Content!.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (!string.IsNullOrEmpty(bearer)) clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return clone;
    }
}
