using System.Net;
using System.Net.Http.Headers;
using Animora.Application.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Sync.Custom;

/// <summary>
/// G06 delegating handler for the shared named <see cref="HttpClient"/>: attaches <c>Authorization: Bearer &lt;access&gt;</c> from the
/// <see cref="ISessionStore"/> and, on a 401 from a protected route, performs exactly ONE single-flight refresh
/// (<see cref="ISessionRefresher"/>) and retries the request once with the rotated token. A second 401 — or a refresh that fails
/// (<c>SESSION_EXPIRED</c>) — surfaces the 401 to the caller (which maps it to <c>UNAUTHORIZED</c>); there is never a refresh loop.
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

        // Buffer once so the body can be replayed on the single retry.
        byte[]? body = null;
        if (request.Content is not null) body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        using var first = Clone(request, body, usedToken);
        var response = await base.SendAsync(first, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || usedToken is null) return response;

        var refreshed = await RefreshOnceAsync(usedToken, cancellationToken).ConfigureAwait(false);
        if (refreshed is null) return response; // refresh failed / session gone: surface the original 401

        response.Dispose();
        using var retry = Clone(request, body, refreshed);
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false); // a second 401 goes straight to the caller
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
