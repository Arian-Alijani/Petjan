using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Errors;
using Animora.Sync.Custom;

namespace Animora.Client.Services.Identity;

/// <summary>
/// The one HTTP seam of the online identity plane: sends JSON requests over the shared named client (<see cref="SyncFeedClient.HttpClientName"/>,
/// so the bearer handler is in the pipeline) with the wire-contract header. Network faults and timeouts never throw — they come back as an
/// <see cref="Exchange"/> without a response so callers can fall back to the offline path; only caller cancellation propagates.
/// </summary>
public sealed class OnlineApi(IHttpClientFactory httpFactory, ServerEndpointOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Outcome of one request: a response, or a human-readable reason there is none (server unreachable / timed out).</summary>
    public readonly record struct Exchange(HttpResponseMessage? Response, string? Detail);

    /// <summary>Sends <paramref name="method"/> <paramref name="path"/> (relative to the server root) with an optional JSON body and bearer.</summary>
    /// <param name="bearer">Explicit access token; when <see langword="null"/> the bearer handler attaches the stored session's token (if any).</param>
    public async Task<Exchange> SendAsync(HttpMethod method, string path, object? body, string? bearer, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(WireContract.HeaderName, WireContract.Version.ToString(CultureInfo.InvariantCulture));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        if (!string.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.RequestTimeout);
        try
        {
            var http = httpFactory.CreateClient(SyncFeedClient.HttpClientName);
            // Headers-read: the caller owns the response and disposes it; the body is read by ReadAsync below.
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            return new Exchange(response, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Exchange(null, "The server did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new Exchange(null, ex.Message);
        }
    }

    /// <summary>Reads a JSON body; <see langword="null"/> when it is empty or not the expected shape.</summary>
    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(response);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The stable <c>code</c> of a <see cref="WireApiError"/> body, or <see langword="null"/> when the body is not one.</summary>
    public static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        var error = await ReadAsync<WireApiError>(response, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(error?.Code) ? null : error.Code;
    }

    /// <summary>True for statuses that mean "try again later / server unreachable" rather than a definitive answer.</summary>
    public static bool IsTransient(HttpStatusCode status) => (int)status is 408 or 429 or >= 500;
}
