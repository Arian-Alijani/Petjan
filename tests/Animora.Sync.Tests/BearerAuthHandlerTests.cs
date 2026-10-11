using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Animora.Application.Security;
using Animora.Domain.Results;
using Animora.Sync.Custom;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G06: the shared HttpClient's bearer handler — attach, single-flight refresh on 401, retry exactly once, never loop.</summary>
public sealed class BearerAuthHandlerTests
{
    private static readonly DateTimeOffset Later = FeedFixtures.Now.AddHours(1);

    private sealed class MemorySessions(string? initialAccess) : ISessionStore
    {
        private ClientSession? _session = initialAccess is null ? null : Make(initialAccess);

        public int Cleared { get; private set; }

        public static ClientSession Make(string access) => new(access, Later, "refresh", "U1", "T1", "D1", "Doctor", "STAMP-1");

        public Task<Result<ClientSession?>> ReadAsync(CancellationToken ct = default) => Task.FromResult(Result<ClientSession?>.Ok(_session));

        public Task<Result> WriteAsync(ClientSession session, CancellationToken ct = default)
        {
            _session = session;
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> ClearAsync(CancellationToken ct = default)
        {
            Cleared++;
            _session = null;
            return Task.FromResult(Result.Ok());
        }
    }

    private sealed class ScriptedRefresher(MemorySessions sessions, string? rotateTo, ErrorCode? failWith = null) : ISessionRefresher
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<Result> RefreshAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(25, ct).ConfigureAwait(false); // widen the race window for the concurrency test
            if (failWith is { } error)
            {
                await sessions.ClearAsync(ct).ConfigureAwait(false);
                return Result.Fail(error);
            }

            return await sessions.WriteAsync(MemorySessions.Make(rotateTo!), ct).ConfigureAwait(false);
        }
    }

    private sealed class Recorder(Func<string?, HttpStatusCode> status) : HttpMessageHandler
    {
        private readonly object _gate = new();

        public List<(string? Bearer, string Path, string Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var bearer = request.Headers.Authorization is { Scheme: "Bearer" } auth ? auth.Parameter : null;
            lock (_gate) Seen.Add((bearer, request.RequestUri!.AbsolutePath, body));
            return new HttpResponseMessage(status(bearer));
        }
    }

    private static HttpClient Client(MemorySessions sessions, ISessionRefresher? refresher, Recorder inner)
    {
        var services = new ServiceCollection();
        if (refresher is not null) services.AddSingleton(refresher);
        var handler = new BearerAuthHandler(sessions, services.BuildServiceProvider()) { InnerHandler = inner };
        return new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") };
    }

    private static HttpRequestMessage Post(string path, string body = "{\"x\":1}") =>
        new(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Attaches_Bearer()
    {
        var sessions = new MemorySessions("tok-1");
        var inner = new Recorder(_ => HttpStatusCode.OK);
        using var client = Client(sessions, null, inner);

        using var response = await client.GetAsync("api/commands/x/ids");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("tok-1", Assert.Single(inner.Seen).Bearer);
    }

    [Fact]
    public async Task AnonymousRoutes_AndSignedOutUser_GetNoBearer()
    {
        var inner = new Recorder(_ => HttpStatusCode.OK);
        using (var client = Client(new MemorySessions("tok-1"), null, inner))
        {
            foreach (var path in new[] { "auth/login", "auth/refresh", "licensing/keys", "auth/keys" })
                (await client.PostAsync(path, new StringContent("{}"))).Dispose();
        }

        Assert.All(inner.Seen, s => Assert.Null(s.Bearer));

        var signedOut = new Recorder(_ => HttpStatusCode.OK);
        using (var client = Client(new MemorySessions(null), null, signedOut)) (await client.GetAsync("api/commands/x/ids")).Dispose();
        Assert.Null(Assert.Single(signedOut.Seen).Bearer);
    }

    [Fact]
    public async Task ExplicitAuthorizationHeader_IsLeftAlone()
    {
        var sessions = new MemorySessions("stored");
        var inner = new Recorder(_ => HttpStatusCode.OK);
        using var client = Client(sessions, null, inner);
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/device/register") { Content = new StringContent("{}") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "explicit");

        (await client.SendAsync(request)).Dispose();

        Assert.Equal("explicit", Assert.Single(inner.Seen).Bearer);
    }

    [Fact]
    public async Task On401_SingleFlightRefresh_RetriesOnce()
    {
        var sessions = new MemorySessions("old");
        var refresher = new ScriptedRefresher(sessions, rotateTo: "new");
        var inner = new Recorder(bearer => bearer == "new" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        using var client = Client(sessions, refresher, inner);

        // Five concurrent requests all start with the expired token: exactly ONE refresh serves all of them.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async i =>
        {
            using var request = Post("api/commands", $"{{\"n\":{i}}}");
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }));

        Assert.All(responses, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(1, refresher.Calls);
        // Every request ends up served with the rotated token, with its body replayed intact on the retry.
        Assert.Equal(5, inner.Seen.Count(s => s.Bearer == "new"));
        Assert.Equal(5, inner.Seen.Where(s => s.Bearer == "new").Select(s => s.Body).Distinct().Count());
        Assert.All(inner.Seen, s => Assert.NotEmpty(s.Body));
    }

    [Fact]
    public async Task RefreshSessionExpired_SurfacesUnauthorized_NoLoop()
    {
        var sessions = new MemorySessions("old");
        var refresher = new ScriptedRefresher(sessions, rotateTo: null, failWith: ErrorCodes.SessionExpired);
        var inner = new Recorder(_ => HttpStatusCode.Unauthorized);
        using var client = Client(sessions, refresher, inner);

        using var response = await client.SendAsync(Post("api/commands"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, refresher.Calls);
        Assert.Single(inner.Seen); // no retry: the refresh failed
        Assert.Equal(1, sessions.Cleared);
    }

    [Fact]
    public async Task Second401AfterSuccessfulRefresh_IsSurfaced_NotRefreshedAgain()
    {
        var sessions = new MemorySessions("old");
        var refresher = new ScriptedRefresher(sessions, rotateTo: "new");
        var inner = new Recorder(_ => HttpStatusCode.Unauthorized); // even the fresh token is rejected
        using var client = Client(sessions, refresher, inner);

        using var response = await client.SendAsync(Post("api/commands"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, refresher.Calls);
        Assert.Collection(inner.Seen,
            first => Assert.Equal("old", first.Bearer),
            second => Assert.Equal("new", second.Bearer));
    }

    [Fact]
    public async Task On401_WithoutRefresher_SurfacesTheOriginal401()
    {
        var inner = new Recorder(_ => HttpStatusCode.Unauthorized);
        using var client = Client(new MemorySessions("old"), null, inner);

        using var response = await client.GetAsync("api/commands/x/ids");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(inner.Seen);
    }
}
