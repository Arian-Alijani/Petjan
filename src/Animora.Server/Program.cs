using Animora.Server.Admin;
using Animora.Server.Insight;
using Animora.Server.Observability;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Files;
using Animora.Server.Licensing;
using Animora.Server.Workers;
using Animora.Server.Operator;
using Animora.Server.Provisioning;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Server.Hints;
using Animora.Server.Sync;
using Animora.Persistence.PostgreSql.Replication;
using Microsoft.Extensions.Options;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.KeyRing;
using Animora.Server.Auth;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Animora.Server.Commands;
using Animora.Server.Handlers;
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

// 2. Local-only redacted logging and optional private telemetry.
LoggingSetup.Configure(builder);
builder.Services.AddSingleton<ServerMetrics>();
builder.Services.AddSingleton<ReadinessSnapshot>();
builder.Services.AddSingleton<ReadinessEvaluator>();
builder.Services.AddAnimoraTelemetry(builder.Configuration);
builder.Services.AddSingleton<IReportingQuerySessionFactory,ReportingQuerySessionFactory>();
builder.Services.AddSingleton<QueryBudget>(sp =>
{
    // PERF-QB-01: reporting sessions use the worker pool, so never allow more permits than that pool has connections.
    var db = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Animora.Persistence.PostgreSql.Options.DatabaseOptions>>().Value;
    var qb = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServerOptions>>().Value.QueryBudget;
    return new QueryBudget(sp.GetRequiredService<IReportingQuerySessionFactory>(),
        Math.Clamp(qb.ReportingMax > 0 ? qb.ReportingMax : db.WorkerMax, 1, Math.Max(1, db.WorkerMax)),
        TimeSpan.FromSeconds(Math.Max(1, qb.WaitSeconds)));
});
builder.Services.AddSingleton<IQueryBudget>(sp =>
{
    var budget = sp.GetRequiredService<QueryBudget>();
    sp.GetRequiredService<ServerMetrics>().ObserveReportingBudget(budget); // IMP-04: reporting-budget wait/in-use signals
    return budget;
});
builder.Services.AddSingleton<IDatabaseCapabilityProbe,DatabaseCapabilityProbe>();
builder.Services.AddSingleton<ServerDatabaseStatisticsReader>();
builder.Services.AddSingleton<DatabasePoolUsage>();
builder.Services.AddSingleton<SnapshotAgeMonitor>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<SnapshotAgeMonitor>());
builder.Services.AddSingleton<IReadinessProbe>(sp=>sp.GetRequiredService<SnapshotAgeMonitor>());
builder.Services.AddSingleton<IReadinessProbe,DatabaseCapacityReadinessProbe>();
builder.WebHost.ConfigureKestrel((context,options)=>KestrelProfile.Apply(options,context.Configuration.GetValue("Database:ProfileGiB",2)));
builder.Services.AddResponseCompression(options=>
{
    options.EnableForHttps=true; options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.MimeTypes=["application/json"];
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o=>o.Level=System.IO.Compression.CompressionLevel.Fastest);

// 3. DataProtection: key ring persisted in PostgreSQL (own context + own pool), stable
//    ApplicationName. No in-memory/file fallback — an unreachable ring fails readiness.
builder.Services.AddDataProtection()
    .SetApplicationName("Animora.Server")
    .PersistKeysToDbContext<AnimoraKeyRingDbContext>();

// 4. DbContext: PostgreSQL (keyed pooled data sources, tenancy/version interceptors).
//    The running role has no DDL rights and the host never migrates on startup.
builder.Services.AddAnimoraPostgreSql(builder.Configuration);
builder.Services.AddAnimoraClinicalPersistence();
builder.Services.AddAnimoraSchedulingBoardingPersistence();
builder.Services.AddAnimoraSalesCashierPersistence();
builder.Services.AddAnimoraMoneyStockPersistence();
builder.Services.AddAnimoraServerFinancialEvents();

// 4b. Identity (S04): ASP.NET Core Identity over the Domain User, opaque Data-Protection bearer + refresh
//     tokens (no JWT/OIDC), lockout, device binding, append-only security audit. Authentication only.
builder.Services.AddAnimoraIdentity(builder.Configuration);

// 4c. Tenant context + stored-RBAC authorization + entitlement slot + snapshot signing (S05).
builder.Services.AddAnimoraAuthorization();
builder.Services.AddAnimoraSubscriptions(builder.Configuration);
builder.Services.AddAnimoraOperator();
builder.Services.AddAnimoraProvisioning();
builder.Services.AddAnimoraLicensing(builder.Configuration);
builder.Services.AddAnimoraAdmin(builder.Configuration);
builder.Services.AddAnimoraInsight(builder.Configuration);
builder.Services.AddSingleton<AdminStatusCache>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AdminStatusCache>());

// 4d. Fenced command intake (S06): server Application profile, no desktop journal/session dependencies.
builder.Services.AddAnimoraCommandPlane(
    handlers => { ServerHandlerModuleS07.AddHandlers(handlers); ServerHandlerModuleS08.AddHandlers(handlers); ServerHandlerModuleS09.AddHandlers(handlers); ServerHandlerModuleS10.AddHandlers(handlers); },
    payloads => { ServerHandlerModuleS07.AddPayloads(payloads); ServerHandlerModuleS08.AddPayloads(payloads); ServerHandlerModuleS09.AddPayloads(payloads); ServerHandlerModuleS10.AddPayloads(payloads); });
builder.Services.AddScoped<ServerSchedulingConcurrencyGuard>();
builder.Services.AddScoped<IServerCommandConcurrencyGuard, ServerFinancialConcurrencyGuard>();
builder.Services.AddScoped<ICommandEffectIdCollector, ServerCommandEffectIdCollector>();
builder.Services.AddScoped<IAggregateVersionReader, ClinicalAggregateVersionReader>();

// S12 feed storage shares the normalized, validated server data root.
builder.Services.AddOptions<FeedStorageOptions>().Configure<IOptions<ServerOptions>>(
    (feed, server) => feed.DataRootPath = server.Value.DataRootPath);

builder.Services.AddAnimoraWorkers();
builder.Services.AddAnimoraFiles();

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

// SEC-HSTS-01: when a proxy terminates TLS the redirect target must use the PUBLIC https port (the app only sees
// the proxy's plain-HTTP port). Scheme detection itself comes from the allowlisted X-Forwarded-Proto (see below).
builder.Services.AddOptions<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>()
    .Configure<Microsoft.Extensions.Options.IOptions<ServerOptions>>((redirect, server) =>
    {
        if (server.Value.Tls.Termination == TlsTermination.Proxy)
        {
            redirect.HttpsPort = server.Value.Tls.PublicHttpsPort;
        }
    });

// Rate limiting: five named fixed-window policies (ARCHITECTURE.md §4). Windows
// are options-driven; a rejection becomes a 429 WireApiError + Retry-After.
// Health endpoints are never limited (they carry no [EnableRateLimiting]).
builder.Services.AddRateLimiter(ServerRateLimiting.Configure);

var app = builder.Build();

// 7. Middleware pipeline (fixed order):
//    global exception → forwarded headers → HSTS/HTTPS redirect (non-Development) → request logging
//    (skips /health/*) → authN (token decode, no DB) → rate limiter → tenant binding → authZ →
//    entitlement → antiforgery → endpoints.
app.UseMiddleware<GlobalExceptionMiddleware>();

// SEC-FWD-01: derive the client IP / scheme from a trusted-proxy allowlist BEFORE any
// IP-based decision (rate limiting, auditing) or HTTPS redirect runs. No-op (fail-safe)
// unless Server:ForwardedHeaders:Enabled is set with a KnownProxies/KnownNetworks allowlist.
ServerForwardedHeaders.Use(app);

// TLS contract (SEC-HSTS-01): non-Development always enforces HTTPS in-app. With Server:Tls:Termination=Proxy the
// scheme comes from the allowlisted X-Forwarded-Proto (validator guarantees ForwardedHeaders is enabled), so a
// request the proxy already received over HTTPS is NOT redirected again (no loop); a plain-HTTP client request is.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<RequestLoggingMiddleware>();
// Compression is eligible only for known >=1KiB JSON responses; no sync/file/auth/admin response compression.
app.UseWhen(c=>c.Request.Path.StartsWithSegments(ServerRoutes.Commands),branch=>branch.UseMiddleware<JsonCompressionMiddleware>());
// Plane order is fixed and tested (PlaneOrderTests): authN (401) → tenant bound from the STORED membership →
// permission authZ (403) → entitlement (S16 fills the gate; allow-all until then).
app.UseMiddleware<AdminSecurityHeadersMiddleware>();
// BUG-PIPE-01: the limiter runs BEFORE every database-backed plane (operator cookie validation, membership
// resolution, permission authZ, entitlement) so a flood is throttled before it pays the 5-7 round-trip identity cost.
// UseAuthentication() stays in front of it on purpose: the opaque bearer token is decrypted by Data Protection
// (key ring cached in memory, NO database read), and the tenant+user partition keys of the authenticated policies
// (SEC-RL-01) are derived from that principal. Endpoint policies are resolved by the routing the host adds first.
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<OperatorContextMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<EntitlementMiddleware>();

app.UseAntiforgery();
app.UseMiddleware<AdminAntiforgeryMiddleware>();

if (enableOpenApi)
{
    if (!app.Environment.IsDevelopment())
    {
        // SEC-OPENAPI-01: the schema is anonymous; make an opt-in outside Development impossible to miss in logs.
        app.Logger.LogWarning("OpenAPI schema is exposed anonymously outside Development (Server:EnableOpenApi=true); restrict it to a management network.");
    }

    app.MapOpenApi();
}

// SCALE-STATE-01 / PERF-AUTHZ-01: the authZ verdict cache is per-process; InvalidateUser/Tenant are local-only, so a
// revocation on one replica lags on others by up to the TTL. Make the multi-instance escape hatch operator-visible.
if (app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.AuthCache.TtlSeconds > 0)
{
    app.Logger.LogWarning("AuthZ verdict cache is enabled (Server:AuthCache:TtlSeconds>0) and is per-process; on multi-instance deployments a revocation can lag other replicas by up to the TTL — set Server:AuthCache:TtlSeconds=0 or provide a shared invalidation bus.");
}

// Authentication endpoints (S04) — /auth/login|refresh|logout|device/register under the `auth` rate limit.
app.MapStaticAssets();
app.MapAdminAccountEndpoints();
app.MapAdminBackupEndpoints();
app.MapAdminTenantImportEndpoints();
// App and route pages live in the same RCL, already included by MapRazorComponents.
app.MapRazorComponents<Animora.Web.Admin.App>().AddInteractiveServerRenderMode()
    .RequireAuthorization(OperatorCookie.Policy).RequireRateLimiting(RateLimitPolicies.Operator);
app.MapAuthEndpoints();
app.MapFileEndpoints();
app.MapLicenseEndpoints();
app.MapLicenseOperatorEndpoints();
app.MapOperatorEndpoints();
app.MapProvisioningEndpoints();
app.MapWebsiteClientEndpoints();
app.MapOperatorProvisioningEndpoints();

// Device approve/revoke, capability snapshots (S05) and the anonymous public key set.
app.MapDeviceEndpoints();

// Online staff (clinic user) management: /auth/staff roster, create, enable/disable, permission overrides.
app.MapStaffEndpoints();

// S06 command conversation: independent settlements and server-allocated id maps.
app.MapCommandEndpoints();
app.MapSyncEndpoints();
// D04 authoritative financial read path (invoices/payments since a cursor); separate from command intake + state feed.
app.MapFinanceEndpoints();
app.MapHub<SyncHintHub>(ServerRoutes.Sync + "/hub").RequireAuthorization(CoarsePolicies.TenantMember, CoarsePolicies.DeviceActive)
    .RequireRateLimiting(RateLimitPolicies.Sync);

// Health endpoints — anonymous, never rate-limited, leak no secret (ARCH §4).
app.MapGet(ServerRoutes.HealthLive, () => Results.Json(new { status = "live" }));

// /health/ready serves a short-TTL cached report (BUG-HEALTH-01): polling cost is one probe round per TTL.
app.MapGet(ServerRoutes.HealthReady, async (ReadinessEvaluator readiness, WorkerHeartbeat workers, CancellationToken ct) =>
{
    var report = await readiness.EvaluateAsync(ct).ConfigureAwait(false);
    return report.IsReady
        ? Results.Json(new { status = report.Degraded.Count == 0 ? "ready" : "degraded", degraded = report.Degraded, workers = workers.StaleNames() })
        : Results.Json(new { status = "not-ready", failed = report.Failed, degraded = report.Degraded, workers = workers.StaleNames() }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

// IMP-10: startup probe for orchestrators - 503 until every required probe has been healthy once, then 200 forever
// (a later dependency blip is a readiness concern, never a reason to restart/kill a started instance).
app.MapGet(ServerRoutes.HealthStartup, async (ReadinessEvaluator readiness, CancellationToken ct) =>
{
    if (!readiness.Started)
    {
        await readiness.EvaluateAsync(ct).ConfigureAwait(false);
    }

    return readiness.Started
        ? Results.Json(new { status = "started" })
        : Results.Json(new { status = "starting" }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

// Exposed for WebApplicationFactory<Program> in Animora.Server.Tests.
public partial class Program;
