using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.KeyRing;
using Animora.Server.Auth;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────────────────────
// Fixed startup order (ARCHITECTURE.md §4). The numbering is binding; later
// phases fill the deferred steps without reordering.
// ─────────────────────────────────────────────────────────────────────────────

// 1. Options: bind + ValidateOnStart (a misconfiguration fails at boot, not on
//    first request), then SecretGuard (no literal secret may live in a JSON file).
var contentRoot = builder.Environment.ContentRootPath;
builder.Services
    .AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
    .PostConfigure(options =>
    {
        // Normalize a relative DataRootPath (e.g. the Development "./.data") to an
        // absolute path against the content root, so the absolute-path invariant
        // holds regardless of how it was supplied.
        if (!string.IsNullOrWhiteSpace(options.DataRootPath) && !Path.IsPathRooted(options.DataRootPath))
        {
            options.DataRootPath = Path.GetFullPath(options.DataRootPath, contentRoot);
        }
    })
    .ValidateOnStart();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ServerOptions>, ServerOptionsValidator>();
SecretGuard.ThrowIfSecretsInJsonFiles(builder.Configuration);

// 2. Logging (console/providers are configured by the default host builder).

// 3. DataProtection: key ring persisted in PostgreSQL (own context + own pool), stable
//    ApplicationName. No in-memory/file fallback — an unreachable ring fails readiness.
builder.Services.AddDataProtection()
    .SetApplicationName("Animora.Server")
    .PersistKeysToDbContext<AnimoraKeyRingDbContext>();

// 4. DbContext: PostgreSQL (keyed pooled data sources, tenancy/version interceptors).
//    The running role has no DDL rights and the host never migrates on startup.
builder.Services.AddAnimoraPostgreSql(builder.Configuration);

// 4b. Identity (S04): ASP.NET Core Identity over the Domain User, opaque Data-Protection bearer + refresh
//     tokens (no JWT/OIDC), lockout, device binding, append-only security audit. Authentication only.
builder.Services.AddAnimoraIdentity(builder.Configuration);

// 4c. Tenant context + stored-RBAC authorization + entitlement slot + snapshot signing (S05).
builder.Services.AddAnimoraAuthorization();

// 5. Health checks: readiness probes (data root, database, key ring, schema head).
//    Probes degrade to Unhealthy when PostgreSQL is absent; the host still boots.
builder.Services.AddSingleton<IReadinessProbe, DataRootWritableProbe>();
builder.Services.AddSingleton<IReadinessProbe, DatabaseProbe>();
builder.Services.AddSingleton<IReadinessProbe, KeyRingProbe>();
builder.Services.AddSingleton<IReadinessProbe, SchemaProbe>();

// 6. OpenAPI (first-party) only in Development or when Server:EnableOpenApi=true.
var enableOpenApi = builder.Environment.IsDevelopment() ||
    builder.Configuration.GetValue($"{ServerOptions.SectionName}:{nameof(ServerOptions.EnableOpenApi)}", false);
if (enableOpenApi)
{
    builder.Services.AddOpenApi();
}

// Rate limiting: five named fixed-window policies (ARCHITECTURE.md §4). Windows
// are options-driven; a rejection becomes a 429 WireApiError + Retry-After.
// Health endpoints are never limited (they carry no [EnableRateLimiting]).
builder.Services.AddRateLimiter(ServerRateLimiting.Configure);

var app = builder.Build();

// 7. Middleware pipeline (fixed order):
//    global exception → HSTS/HTTPS redirect (non-Development) → request logging
//    (skips /health/*) → authN → tenant binding → authZ → entitlement → rate limiter →
//    routing → endpoints.
app.UseMiddleware<GlobalExceptionMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Request logging (sampled) skips health endpoints — added with observability in S18.
// Plane order is fixed and tested (PlaneOrderTests): authN (401) → tenant bound from the STORED membership →
// permission authZ (403) → entitlement (S16 fills the gate; allow-all until then).
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<EntitlementMiddleware>();

app.UseRateLimiter();

if (enableOpenApi)
{
    app.MapOpenApi();
}

// Authentication endpoints (S04) — /auth/login|refresh|logout|device/register under the `auth` rate limit.
app.MapAuthEndpoints();

// Device approve/revoke, capability snapshots (S05) and the anonymous public key set.
app.MapDeviceEndpoints();

// Health endpoints — anonymous, never rate-limited, leak no secret (ARCH §4).
app.MapGet(ServerRoutes.HealthLive, () => Results.Json(new { status = "live" }));

app.MapGet(ServerRoutes.HealthReady, async (
    IEnumerable<IReadinessProbe> probes,
    CancellationToken ct) =>
{
    var failed = new List<string>();
    foreach (var probe in probes)
    {
        var result = await probe.CheckAsync(ct).ConfigureAwait(false);
        if (!result.IsHealthy)
        {
            failed.Add(probe.Name);
        }
    }

    return failed.Count == 0
        ? Results.Json(new { status = "ready" })
        : Results.Json(new { status = "not-ready", failed }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

// Exposed for WebApplicationFactory<Program> in Animora.Server.Tests.
public partial class Program;
