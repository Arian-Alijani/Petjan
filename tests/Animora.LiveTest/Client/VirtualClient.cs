using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Auth;
using Animora.Contracts.Wire.Commands;
using Animora.Contracts.Wire.Errors;
using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Time;
using Animora.LiveTest.Config;
using Animora.Sync.Custom;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.LiveTest.Client;

/// <summary>
/// One simulated desktop installation talking to the LIVE server over real HTTP. It composes the same production
/// pieces a shipped desktop uses — <see cref="HttpCommandTransport"/> (command intake), <see cref="SyncFeedClient"/>
/// (state pull) and <see cref="BearerAuthHandler"/> (bearer + single-flight refresh) — so every request is
/// byte-identical to what a real client sends. Only the session store (memory) and the replica (in-memory
/// <see cref="ReplicaView"/>) differ, because the tool measures the server, not the client's local store.
/// </summary>
public sealed class VirtualClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ServiceProvider _services;
    private readonly MemorySessionStore _sessions;
    private readonly IHttpClientFactory _httpFactory;
    private readonly HttpCommandTransport _commands;
    private readonly SyncFeedClient _feed;
    private readonly LiveTestOptions _options;
    private readonly string _installationHash;
    private long _localSequence;

    private VirtualClient(string label, int slot, LiveTestOptions options, ServiceProvider services, MemorySessionStore sessions,
        string installationHash)
    {
        Label = label;
        Slot = slot;
        _options = options;
        _services = services;
        _sessions = sessions;
        _installationHash = installationHash;
        _httpFactory = services.GetRequiredService<IHttpClientFactory>();
        _commands = services.GetRequiredService<HttpCommandTransport>();
        _feed = services.GetRequiredService<SyncFeedClient>();
    }

    /// <summary>Human label (e.g. "A", "B", "w07") used in reports and the device name.</summary>
    public string Label { get; }

    /// <summary>Stable device slot: the same slot always maps to the same installation hash, so reruns reuse the device row.</summary>
    public int Slot { get; }

    /// <summary>This client's in-memory view of the replicated state.</summary>
    public ReplicaView Replica { get; } = new();

    /// <summary>Tenant id from the live session (empty until <see cref="LoginAsync"/> succeeds).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>User id from the live session.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Device id from the live session.</summary>
    public string DeviceId { get; private set; } = string.Empty;

    /// <summary>Role name granted to the logged-in user.</summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>True once a bearer session exists.</summary>
    public bool IsLoggedIn => UserId.Length > 0;

    /// <summary>Builds a client wired to <see cref="LiveTestOptions.ServerBaseUrl"/>; does not touch the network.</summary>
    public static VirtualClient Create(LiveTestOptions options, string label, int slot = 0)
    {
        ArgumentNullException.ThrowIfNull(options);
        var services = new ServiceCollection();
        var sessions = new MemorySessionStore();
        services.AddSingleton<Animora.Application.Security.ISessionStore>(sessions);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISessionRefresher>(sp => new LiveSessionRefresher(
            sessions, sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IClock>(), options.RequestTimeoutSeconds));
        services.AddAnimoraHttpSyncEngine(o =>
        {
            o.BaseAddress = options.ServerBaseUrl;
            o.TenantCode = options.TenantCode;
            o.RequestTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        var provider = services.BuildServiceProvider();
        var hash = InstallationHashFor(options, slot);
        return new VirtualClient(label, slot, options, provider, sessions, hash);
    }

    /// <summary>
    /// Deterministic per-slot installation hash (SHA-256 hex, 64 chars ≤ server limit 128). Stable across runs so the
    /// dedicated test tenant never accumulates one device row per run (the licence caps devices).
    /// </summary>
    public static string InstallationHashFor(LiveTestOptions options, int slot)
    {
        var seed = $"animora-livetest|{options.TenantCode}|{options.Username}|{slot.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
    }

    /// <summary>
    /// <c>POST /auth/login</c> (retrying on 429 per Retry-After), then best-effort <c>POST /auth/device/register</c>
    /// to label the device row. Returns null on success, else a one-line English error.
    /// </summary>
    public async Task<string?> LoginAsync(CancellationToken ct = default)
    {
        var request = new LoginRequest(_options.TenantCode.Trim(), _options.Username.Trim(), _options.Password, _installationHash);

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            response?.Dispose();
            var (resp, error) = await SendAsync(HttpMethod.Post, "auth/login", request, bearer: null, ct).ConfigureAwait(false);
            if (resp is null) return $"login: {error}";
            response = resp;
            if (response.StatusCode != HttpStatusCode.TooManyRequests) break;
            await Task.Delay(RetryDelay(response, attempt), ct).ConfigureAwait(false);
        }

        using (response)
        {
            if (!response!.IsSuccessStatusCode) return $"login: {await DescribeFailureAsync(response, ct).ConfigureAwait(false)}";

            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json, ct).ConfigureAwait(false);
            if (login is null || string.IsNullOrEmpty(login.AccessToken) || string.IsNullOrEmpty(login.RefreshToken))
                return "login: response is incomplete (missing tokens).";
            if (login.MustChangePassword)
                return "login: the test account must change its password first (sign in once with the desktop app).";

            await _sessions.WriteAsync(new ClientSession(login.AccessToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, login.ExpiresInSeconds)), login.RefreshToken, login.UserId,
                login.TenantId, login.DeviceId, login.Role, SecurityStamp: string.Empty), ct).ConfigureAwait(false);
            TenantId = login.TenantId;
            UserId = login.UserId;
            DeviceId = login.DeviceId;
            Role = login.Role;

            // Label the device row (best effort: a failure here must not fail the login).
            var register = new DeviceRegisterRequest($"{_options.DeviceNamePrefix}-{Label}", _installationHash, "linux · livetest");
            var (regResp, _) = await SendAsync(HttpMethod.Post, "auth/device/register", register, login.AccessToken, ct).ConfigureAwait(false);
            regResp?.Dispose();

            // Mirror the shipped desktop client (EntitlementSealer): a tenant-wide grant alone does NOT entitle a
            // device — the server issues a per-device licence row and every business route is gated on it
            // (403 ENTITLEMENT_REQUIRED / license_not_active). Without this step login succeeds but every
            // authorized call fails, so preflight's sync/manifest check could never pass.
            // The licensing surface shares the per-IP auth rate-limit bucket (10/min), so a burst of runs can
            // answer 429; retry with the same backoff the login path uses instead of failing the whole suite.
            HttpResponseMessage? licResp = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                licResp?.Dispose();
                var (resp, error) = await SendAsync(HttpMethod.Post, "licensing/activate",
                    new LicenseRequest(_installationHash, CurrentSequence: 0), login.AccessToken, ct).ConfigureAwait(false);
                if (resp is null) return $"licensing/activate: {error}";
                licResp = resp;
                if (resp.StatusCode != HttpStatusCode.TooManyRequests) break;
                await Task.Delay(RetryDelay(resp, attempt), ct).ConfigureAwait(false);
            }

            using (licResp)
            {
                if (licResp is null || !licResp.IsSuccessStatusCode)
                    return $"licensing/activate: {await DescribeFailureAsync(licResp!, ct).ConfigureAwait(false)}";
            }
        }

        return null;
    }

    /// <summary><c>POST /auth/logout</c> (best effort) and drops the local session.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var read = await _sessions.ReadAsync(ct).ConfigureAwait(false);
        if (read.IsSuccess && read.Value is { } session)
        {
            var (resp, _) = await SendAsync(HttpMethod.Post, "auth/logout", body: null, session.AccessToken, ct).ConfigureAwait(false);
            resp?.Dispose();
        }

        await _sessions.ClearAsync(ct).ConfigureAwait(false);
        TenantId = UserId = DeviceId = Role = string.Empty;
    }

    // ───────────────────────────── commands ─────────────────────────────

    /// <summary>
    /// Builds a REAL envelope for a production use-case command exactly like the client's CommandJournalBehavior:
    /// canonical payload JSON + SHA-256 fingerprint (reusing <see cref="CommandFingerprint"/>, never re-derived).
    /// </summary>
    public CommandEnvelope BuildEnvelope<TCommand>(TCommand command, string? operationId = null, string? dependsOnOperationId = null,
        string? expectedServerVersion = null, long? expectedAggregateVersion = null)
        where TCommand : IJournaledCommand
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureLoggedIn();
        var payload = CommandFingerprint.CanonicalJson(command);
        var fingerprint = CommandFingerprint.Compute(command.CommandTypeName, payload);
        return CommandEnvelope.Create(operationId ?? IdGenerator.NewId(), TenantId, UserId, DeviceId, command.CommandTypeName,
            command.AggregateTypeName, command.AggregateIdOrNull, payload, fingerprint, DateTimeOffset.UtcNow,
            Interlocked.Increment(ref _localSequence), causationId: null, dependsOnOperationId, expectedAggregateVersion,
            expectedServerVersion);
    }

    /// <summary>Builds an envelope from a raw type + payload (used for deliberately malformed / replayed cases).</summary>
    public CommandEnvelope BuildRawEnvelope(string commandType, string aggregateType, string? aggregateId, string payloadJson,
        string? operationId = null, string? dependsOnOperationId = null, string? expectedServerVersion = null)
    {
        EnsureLoggedIn();
        return CommandEnvelope.Create(operationId ?? IdGenerator.NewId(), TenantId, UserId, DeviceId, commandType, aggregateType,
            aggregateId, payloadJson, CommandFingerprint.Compute(commandType, payloadJson), DateTimeOffset.UtcNow,
            Interlocked.Increment(ref _localSequence), null, dependsOnOperationId, null, expectedServerVersion);
    }

    /// <summary>Submits one envelope through the real <c>POST /api/commands</c> transport. Never throws on server answers.</summary>
    public async Task<SubmitOutcome> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _commands.SubmitAsync(envelope, ct).ConfigureAwait(false);
            sw.Stop();
            return result.IsSuccess
                ? new SubmitOutcome(envelope, result.Value, null, sw.Elapsed)
                : new SubmitOutcome(envelope, null, $"{result.Error.Code}{(result.Detail is null ? string.Empty : $": {result.Detail}")}", sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SubmitOutcome(envelope, null, "request timed out", sw.Elapsed);
        }
    }

    /// <summary>
    /// Submits and, while the answer is <c>Deferred</c> (429/5xx/transport), resubmits the SAME operation id with
    /// backoff — exactly what the production processor does. Server back-pressure is therefore not a failure.
    /// </summary>
    public async Task<SubmitOutcome> SubmitWithRetryAsync(CommandEnvelope envelope, int maxAttempts = 6, CancellationToken ct = default)
    {
        SubmitOutcome last = await SubmitAsync(envelope, ct).ConfigureAwait(false);
        for (var attempt = 1; attempt < maxAttempts && last.IsDeferred; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(8000, 500 * Math.Pow(2, attempt - 1))), ct).ConfigureAwait(false);
            last = await SubmitAsync(envelope, ct).ConfigureAwait(false);
        }

        return last;
    }

    /// <summary>Submits up to 50 envelopes in ONE <c>POST /api/commands/batch</c> (order is a server contract).</summary>
    public async Task<(IReadOnlyList<SettlementResult>? Results, string? Error, TimeSpan Elapsed)> SubmitBatchAsync(
        IReadOnlyList<CommandEnvelope> envelopes, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await _commands.SubmitBatchAsync(envelopes, ct).ConfigureAwait(false);
        sw.Stop();
        return result.IsSuccess
            ? (result.Value, null, sw.Elapsed)
            : (null, $"{result.Error.Code}{(result.Detail is null ? string.Empty : $": {result.Detail}")}", sw.Elapsed);
    }

    // ───────────────────────────── state pull ─────────────────────────────

    /// <summary>
    /// Pulls the state feed into <see cref="Replica"/>: a full snapshot the first time (or when the server demands a
    /// resync), incremental batches afterwards. 503 + Retry-After back-pressure is retried, then reported as
    /// <see cref="PullKind.Busy"/> — never as a defect.
    /// </summary>
    public async Task<PullResult> PullAsync(CancellationToken ct = default)
    {
        EnsureLoggedIn();
        var sw = Stopwatch.StartNew();
        var resyncs = 0;
        while (true)
        {
            var snapshot = !Replica.HasSnapshot;
            Result<OpenBatchOutcome> opened;
            if (snapshot)
            {
                opened = await RetryBusyAsync(() => _feed.OpenSnapshotAsync(ct), ct).ConfigureAwait(false);
            }
            else
            {
                var manifest = await RetryBusyAsync(() => _feed.GetManifestAsync(ct), ct).ConfigureAwait(false);
                if (manifest.IsFailure) return Fail(manifest.Error, manifest.Detail, sw);
                var epoch = manifest.Value!.Epoch;
                opened = await RetryBusyAsync(() => _feed.OpenBatchAsync(epoch, Replica.Watermark, ct), ct).ConfigureAwait(false);
            }

            if (opened.IsFailure) return Fail(opened.Error, opened.Detail, sw);

            var outcome = opened.Value!;
            switch (outcome.Kind)
            {
                case OpenBatchKind.Current:
                    return new PullResult(PullKind.UpToDate, 0, outcome.Watermark, snapshot, null, sw.Elapsed);

                case OpenBatchKind.Resync:
                    if (++resyncs > 2) return new PullResult(PullKind.Failed, 0, Replica.Watermark, snapshot, $"server demanded resync repeatedly ({outcome.Reason})", sw.Elapsed);
                    Replica.Reset();
                    continue;

                case OpenBatchKind.Ready:
                    var batch = outcome.Batch!;
                    var staged = new List<Animora.Contracts.Wire.Sync.SyncRecord>();
                    for (var ordinal = 0; ordinal < batch.PageCount; ordinal++)
                    {
                        var ord = ordinal;
                        var page = await RetryBusyAsync(() => _feed.GetPageAsync(batch.BatchId, ord, null, ct), ct).ConfigureAwait(false);
                        if (page.IsFailure) return Fail(page.Error, page.Detail, sw);
                        if (page.Value is { } p) staged.AddRange(p.Records);
                    }

                    if (snapshot) Replica.Reset();
                    Replica.Apply(staged);
                    Replica.Commit(batch.Epoch, batch.Watermark, snapshot);
                    return new PullResult(PullKind.Applied, staged.Count, batch.Watermark, snapshot, null, sw.Elapsed);
            }
        }
    }

    private static PullResult Fail(ErrorCode error, string? detail, Stopwatch sw) =>
        new(error.Code == ErrorCodes.ServerBusy.Code ? PullKind.Busy : PullKind.Failed, 0, 0, false,
            $"{error.Code}{(detail is null ? string.Empty : $": {detail}")}", sw.Elapsed);

    private static async Task<Result<T>> RetryBusyAsync<T>(Func<Task<Result<T>>> call, CancellationToken ct)
    {
        Result<T> result = default;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            result = await call().ConfigureAwait(false);
            if (result.IsSuccess || result.Error.Code != ErrorCodes.ServerBusy.Code) return result;
            var seconds = long.TryParse(result.Detail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 1;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 10)), ct).ConfigureAwait(false);
        }

        return result;
    }

    // ───────────────────────────── plain authorized reads ─────────────────────────────

    /// <summary>Authorized (or anonymous, for <c>health/*</c>) GET returning status + body text. Never throws on HTTP errors.</summary>
    public async Task<(HttpStatusCode? Status, string Body, TimeSpan Elapsed)> GetAsync(string relativePath, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        try
        {
            var http = _httpFactory.CreateClient(SyncFeedClient.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            request.Headers.TryAddWithoutValidation(WireContract.HeaderName, WireContract.Version.ToString(CultureInfo.InvariantCulture));
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return (response.StatusCode, body, sw.Elapsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return (null, ex.Message, sw.Elapsed);
        }
    }

    /// <summary>Authorized POST with an optional JSON body; returns status + body text.</summary>
    public async Task<(HttpStatusCode? Status, string Body, TimeSpan Elapsed)> PostAsync(string relativePath, object? body = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var (resp, error) = await SendAsync(HttpMethod.Post, relativePath, body, bearer: null, ct).ConfigureAwait(false);
        if (resp is null) return (null, error ?? "no response", sw.Elapsed);
        using (resp) return (resp.StatusCode, await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false), sw.Elapsed);
    }

    // ───────────────────────────── internals ─────────────────────────────

    private void EnsureLoggedIn()
    {
        if (!IsLoggedIn) throw new InvalidOperationException($"Virtual client '{Label}' has no live session — call LoginAsync first.");
    }

    private async Task<(HttpResponseMessage? Response, string? Error)> SendAsync(HttpMethod method, string path, object? body,
        string? bearer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        try
        {
            var http = _httpFactory.CreateClient(SyncFeedClient.HttpClientName);
            using var request = new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation(WireContract.HeaderName, WireContract.Version.ToString(CultureInfo.InvariantCulture));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            if (!string.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return (response, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return (null, ex is OperationCanceledException ? "request timed out" : ex.Message);
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retry = response.Headers.RetryAfter?.Delta;
        return retry is { } d && d > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Min(30, d.TotalSeconds))
            : TimeSpan.FromSeconds(Math.Min(20, Math.Pow(2, attempt + 1)));
    }

    internal static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string code = "-";
        try
        {
            var error = await response.Content.ReadFromJsonAsync<WireApiError>(Json, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(error?.Code)) code = error.Code;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) { /* non-JSON body */ }

        return $"HTTP {(int)response.StatusCode} code={code}";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try { await LogoutAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException) { /* shutdown is best-effort */ }
        await _services.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Single-flight <c>POST /auth/refresh</c> used by <see cref="BearerAuthHandler"/> when a long run outlives the
/// access token (same contract as the desktop's OnlineAuthClient.RefreshAsync).
/// </summary>
internal sealed class LiveSessionRefresher(MemorySessionStore sessions, IHttpClientFactory httpFactory, IClock clock, int timeoutSeconds)
    : ISessionRefresher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        var current = await sessions.ReadAsync(ct).ConfigureAwait(false);
        if (current.Value is not { } session) return Result.Fail(ErrorCodes.SessionExpired, "no_session");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var http = httpFactory.CreateClient(SyncFeedClient.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/refresh")
            {
                Content = JsonContent.Create(new RefreshRequest(session.RefreshToken), options: Json),
            };
            request.Headers.TryAddWithoutValidation(WireContract.HeaderName, WireContract.Version.ToString(CultureInfo.InvariantCulture));
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await sessions.ClearAsync(ct).ConfigureAwait(false);
                return Result.Fail(ErrorCodes.SessionExpired, $"refresh answered {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(Json, timeout.Token).ConfigureAwait(false);
            if (body is null || string.IsNullOrEmpty(body.AccessToken) || string.IsNullOrEmpty(body.RefreshToken))
                return Result.Fail(ErrorCodes.SyncProtocolViolation, "Refresh response is incomplete.");
            return await sessions.WriteAsync(new ClientSession(body.AccessToken, clock.UtcNow.AddSeconds(Math.Max(0, body.ExpiresInSeconds)),
                body.RefreshToken, body.UserId, body.TenantId, body.DeviceId, body.Role, session.SecurityStamp), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return Result.Fail(ErrorCodes.NetworkUnavailable, ex.Message);
        }
    }
}
