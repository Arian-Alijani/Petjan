using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Time;
using Animora.Sync.Custom;

namespace Animora.Sync.Tests;

/// <summary>Fixed clock shared by the sync tests.</summary>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

/// <summary>Scripted <see cref="HttpMessageHandler"/>: every request is answered by a delegate and recorded.</summary>
internal sealed class FakeFeedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
        return Task.FromResult(respond(request));
    }
}

/// <summary>Builds wire-faithful fixtures: gzip pages whose ETag is the SHA-256 of the raw gzip bytes (as the real server does).</summary>
internal static class FeedFixtures
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    public static readonly DateTimeOffset Now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    public static SyncRecord Record(string id) =>
        new("Pet", id, 'U', "v1", Now, false, JsonDocument.Parse("{\"name\":\"rex\"}").RootElement.Clone());

    /// <summary>A page whose server-side JSON PageHash is intentionally empty (the real server cannot hash itself).</summary>
    public static SyncPage Page(string batchId, int ordinal, int pageCount, int records) =>
        new(batchId, ordinal, pageCount, Enumerable.Range(0, records).Select(i => Record($"{ordinal}-{i}")).ToList(), "");

    public static byte[] Gzip(SyncPage page)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            JsonSerializer.Serialize(gzip, page, Json);
        return output.ToArray();
    }

    public static HttpResponseMessage PageResponse(byte[] gzip)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(gzip) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        response.Headers.ETag = new EntityTagHeaderValue("\"" + PageIntegrity.ComputePageHash(gzip) + "\"");
        return response;
    }

    public static HttpResponseMessage JsonResponse<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: Json) };

    public static SyncFeedClient Client(FakeFeedHandler handler, Action<ServerEndpointOptions>? configure = null, ServerEndpointOptions? options = null)
    {
        options ??= new ServerEndpointOptions { BaseAddress = "https://sync.test/", RetryBaseDelay = TimeSpan.Zero };
        configure?.Invoke(options);
        return new SyncFeedClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") }, options);
    }

    public static ServerEndpointOptions Options() => new() { BaseAddress = "https://sync.test/", RetryBaseDelay = TimeSpan.Zero };
}
