using Animora.Server.Hosting;

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

// 3. DataProtection → S02 (persisted key ring, stable ApplicationName).

// 4. DbContext → S02 (PostgreSQL; the running role has no DDL rights).

// 5. Health checks: readiness probes. S01 ships the data-root writability probe;
//    S02 adds DatabaseProbe + KeyRingProbe.
builder.Services.AddSingleton<IReadinessProbe, DataRootWritableProbe>();

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
//    (skips /health/*) → [tenant → authN → authZ → entitlement : S04/S05] →
//    rate limiter → routing → endpoints.
app.UseMiddleware<GlobalExceptionMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Request logging (sampled) skips health endpoints — added with observability in S18.
// [tenant/authN/authZ/entitlement slots] → S04/S05 (S16 fills entitlement).

app.UseRateLimiter();

if (enableOpenApi)
{
    app.MapOpenApi();
}

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
