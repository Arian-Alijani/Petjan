using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Client.Services.Identity;
using Animora.Contracts.Wire.Auth;
using Animora.Contracts.Wire.Errors;
using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Identity;
using Animora.Persistence.Tests;
using Animora.Server.Licensing.Issuance;
using Animora.Sync.Custom;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Sync.Tests;

/// <summary>Scripted HTTP handler that also records every request body, so tests can prove what did (not) leave the machine.</summary>
internal sealed class RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(string Method, string Path, string Body)> Requests { get; } = [];

    public bool Unreachable { get; set; }

    public int Count(string path) => Requests.Count(r => r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        lock (Requests) Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath.TrimStart('/'), body));
        if (Unreachable) throw new HttpRequestException("simulated: server unreachable");
        return respond(request, body);
    }
}

/// <summary>
/// A tiny wire-faithful stand-in for the Animora server's identity surface (<c>/auth/*</c>, <c>/licensing/*</c>): real ECDSA signatures for
/// the capability snapshot and the licence, so the production verification code runs unmodified end to end.
/// </summary>
internal sealed class FakeIdentityServer
{
    public const string TenantCode = "clinic";
    public const string UserName = "dr.sara";
    public const string Password = "S3cret!pass";

    private readonly ECDsa _capabilityKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _capabilityKeyId = IdGenerator.NewId();

    public FakeIdentityServer(DateTimeOffset now, string installationHash, TimeSpan capabilityWindow)
    {
        Now = now;
        InstallationHash = installationHash;
        CapabilityWindow = capabilityWindow;
        Licenses = new TestLicenseAuthority(now.AddDays(-1));
        TenantId = IdGenerator.NewId();
        UserId = IdGenerator.NewId();
        DeviceId = IdGenerator.NewId();
        Handler = new RecordingHandler(Respond);
    }

    public DateTimeOffset Now { get; }

    public string InstallationHash { get; }

    public TimeSpan CapabilityWindow { get; }

    public TestLicenseAuthority Licenses { get; }

    public string TenantId { get; set; }

    public string UserId { get; }

    public string DeviceId { get; }

    public RecordingHandler Handler { get; }

    /// <summary>When set, <c>POST /auth/login</c> answers this (status, wire error code) instead of succeeding.</summary>
    public (HttpStatusCode Status, string Code)? LoginFailure { get; set; }

    /// <summary>When set, <c>POST /auth/device/register</c> answers this wire error code with 403.</summary>
    public string? DeviceRegisterFailureCode { get; set; }

    /// <summary>Device status <c>POST /auth/device/register</c> reports on success (<c>Active</c> by default; <c>Pending</c> for unapproved staff devices).</summary>
    public string DeviceRegisterStatus { get; set; } = "Active";

    public string LastAccessToken { get; private set; } = string.Empty;

    private HttpResponseMessage Respond(HttpRequestMessage request, string body)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        switch (path)
        {
            case "auth/login":
                if (LoginFailure is { } failure) return Error(failure.Status, failure.Code);
                LastAccessToken = "access-" + Guid.NewGuid().ToString("N");
                return Json(new LoginResponse(LastAccessToken, 900, "refresh-" + Guid.NewGuid().ToString("N"), UserId, TenantId, DeviceId, "Doctor", false));
            case "auth/device/register":
                return DeviceRegisterFailureCode is { } code
                    ? Error(HttpStatusCode.Forbidden, code)
                    : Json(new DeviceRegisterResponse(DeviceId, DeviceRegisterStatus));
            case "auth/keys":
                return Json(new PublicKeySet([new PublicKeyInfo(_capabilityKeyId, "ECDSA-P256", Convert.ToBase64String(_capabilityKey.ExportSubjectPublicKeyInfo()), null)]));
            case "auth/capabilities":
                return Json(CapabilitySnapshot());
            case "licensing/keys":
                return Json(Licenses.ToWire(Now));
            case "licensing/activate":
            case "licensing/renew":
                var payload = TestLicenseAuthority.Payload(TenantId, InstallationHash, Now);
                return Json(new LicenseResponse(Licenses.Issue(payload), payload.ExpiresUtc, payload.OfflineGraceUntilUtc, payload.Sequence, Now));
            case "auth/logout":
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private CapabilitySnapshotResponse CapabilitySnapshot()
    {
        var expiry = Now.Add(CapabilityWindow).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var issued = Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var payload = JsonSerializer.Serialize(new
        {
            tenantId = TenantId,
            userId = UserId,
            deviceId = DeviceId,
            securityStamp = "STAMP-1",
            issuedUtc = issued,
            snapshotExpiryUtc = expiry,
            capabilityExpiry = new Dictionary<string, string> { ["patients.read"] = expiry, ["sales.post"] = expiry },
        });
        var signature = _capabilityKey.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new CapabilitySnapshotResponse(payload, _capabilityKeyId, Convert.ToBase64String(signature));
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: FeedFixtures.Json) };

    private static HttpResponseMessage Error(HttpStatusCode status, string code) =>
        new(status) { Content = JsonContent.Create(new WireApiError(code, null, [], "corr"), options: FeedFixtures.Json) };
}

/// <summary>A fully wired client-side online-identity stack over a real migrated SQLite file and the <see cref="FakeIdentityServer"/>.</summary>
internal sealed class OnlineStack : IDisposable
{
    public static readonly string[] RawComponents = ["machine:SECRET-HOST-NAME", "install:11111111-2222-3333-4444-555555555555", "os:Unix"];

    private OnlineStack(LocalStoreTestFixture fixture, FakeIdentityServer server, FakeMonotonicClock monotonic)
    {
        Fixture = fixture;
        Server = server;
        Monotonic = monotonic;
    }

    public LocalStoreTestFixture Fixture { get; }

    public FakeIdentityServer Server { get; }

    public FakeMonotonicClock Monotonic { get; }

    public IServiceProvider Services => Fixture.Services;

    public static async Task<OnlineStack> CreateAsync(TimeSpan? capabilityWindow = null)
    {
        var now = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
        var fingerprint = new FixedFingerprintSource(RawComponents);
        var installation = Animora.Client.Services.Identity.InstallationHash.Compute(fingerprint);
        var server = new FakeIdentityServer(now, installation, capabilityWindow ?? TimeSpan.FromDays(7));
        var monotonic = new FakeMonotonicClock(now);

        var fixture = new LocalStoreTestFixture(now: now, followInstallation: true, configureServices: services =>
        {
            services.AddSingleton<Animora.Infrastructure.Local.Security.ISecretStore>(new MemorySecretStore());
            services.AddSingleton<Animora.Application.Security.IPasswordHasher>(new PlainPasswordHasher());
            services.AddSingleton<IDeviceFingerprintSource>(fingerprint);
            services.AddSingleton<IMonotonicClock>(monotonic);
            services.AddAnimoraHttpSyncEngine(o => o.BaseAddress = "https://animora.example/");
            services.AddHttpClient(SyncFeedClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => server.Handler);
            services.AddAnimoraOnlineIdentity();
        });
        await fixture.InitializeAsync();
        return new OnlineStack(fixture, server, monotonic);
    }

    public Animora.Client.Services.Identity.IOnlineAuthClient Client =>
        Services.GetRequiredService<Animora.Client.Services.Identity.IOnlineAuthClient>();

    public void Dispose() => Fixture.Dispose();
}
