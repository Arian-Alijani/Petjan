using System.Text.Json;

namespace Animora.LiveTest.Config;

/// <summary>
/// All inputs the live test tool needs, resolved from (in order of precedence):
/// environment variables, an optional <c>livetest.json</c> file, then built-in defaults.
/// Secrets (password, SSH key path) are NEVER hard-coded — the operator supplies them at
/// run time. The tool only ever targets the dedicated test tenant.
/// </summary>
public sealed class LiveTestOptions
{
    /// <summary>Absolute http(s) base address of the live central server.</summary>
    public string ServerBaseUrl { get; set; } = "http://188.212.99.234:5000";

    /// <summary>Clinic/tenant code of the DEDICATED test tenant (licensed via grant-test-subscription.sh).</summary>
    public string TenantCode { get; set; } = "ANIMORA-LOADTEST";

    /// <summary>Username of the test tenant's manager account.</summary>
    public string Username { get; set; } = "";

    /// <summary>Password of the test tenant's manager account (env/secret only).</summary>
    public string Password { get; set; } = "";

    /// <summary>Device display name prefix registered per virtual client.</summary>
    public string DeviceNamePrefix { get; set; } = "livetest";

    /// <summary>Per-request timeout seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum distinct devices (logins) the capacity run uses. The licence caps devices per tenant (seats × 2), so
    /// concurrency above this value is produced by several virtual users sharing one device session.
    /// </summary>
    public int MaxDevices { get; set; } = 8;

    /// <summary>Capacity: a level is "unhealthy" when its p95 latency exceeds this many milliseconds.</summary>
    public double P95LimitMs { get; set; } = 2000;

    /// <summary>Capacity: run only profiles whose name contains this text (case-insensitive).</summary>
    public string ProfileFilter { get; set; } = "";

    /// <summary>Capacity: allow a tenant code that does not look like a test tenant (default refuses, to protect real clinics).</summary>
    public bool AllowAnyTenant { get; set; }

    /// <summary>Concurrency levels (virtual clients) to ramp through during capacity runs.</summary>
    public int[] ConcurrencyLevels { get; set; } = [1, 2, 4, 8, 16, 32];

    /// <summary>Operations per concurrency level per profile (used only when <see cref="LevelDurationSeconds"/> is 0).</summary>
    public int RequestsPerLevel { get; set; } = 60;

    /// <summary>
    /// Auto-ramp: ignore <see cref="ConcurrencyLevels"/> and climb <see cref="RampStart"/> → <see cref="RampMax"/>
    /// in <see cref="RampStep"/> increments, stopping once the server has been unhealthy for
    /// <see cref="BreakConfirmations"/> consecutive levels. This finds the real ceiling instead of testing one
    /// hand-picked number.
    /// </summary>
    public bool AutoRamp { get; set; }

    /// <summary>Auto-ramp: concurrency of the first level.</summary>
    public int RampStart { get; set; } = 5;

    /// <summary>Auto-ramp: how many virtual users each level adds.</summary>
    public int RampStep { get; set; } = 5;

    /// <summary>Auto-ramp: hard stop. The ramp also stops earlier as soon as the server breaks.</summary>
    public int RampMax { get; set; } = 200;

    /// <summary>
    /// Auto-ramp: how many consecutive unhealthy levels confirm the ceiling. 2 avoids declaring a break on a single
    /// noisy window (one cold-start GC pause, one disk flush).
    /// </summary>
    public int BreakConfirmations { get; set; } = 2;

    /// <summary>
    /// Length of each level's measurement window in seconds. Levels are time-boxed rather than request-counted so a
    /// level at 5 users and a level at 150 users are directly comparable (and think time can be modelled at all).
    /// Set to 0 to go back to fixed <see cref="RequestsPerLevel"/> budgets.
    /// </summary>
    public int LevelDurationSeconds { get; set; } = 20;

    /// <summary>
    /// Shortest pause a virtual user takes between two actions, in milliseconds. A real clerk reads the screen,
    /// types, and moves the mouse between requests; a zero-pause loop models a bot, not a clinic, and inflates the
    /// measured ceiling by an order of magnitude.
    /// </summary>
    public int ThinkTimeMinMs { get; set; } = 2000;

    /// <summary>Longest pause between two actions (the distribution is log-uniform, so most pauses are short).</summary>
    public int ThinkTimeMaxMs { get; set; } = 9000;

    /// <summary>
    /// Background state-feed poll per device session, in seconds — the cadence the production desktop uses
    /// (<c>LiveAccessRefreshService</c> polls every 4–6 s). 0 disables it. Without it the server would see less
    /// traffic per desktop than a real clinic generates.
    /// </summary>
    public int AmbientPollSeconds { get; set; } = 5;

    /// <summary>Seconds of cooldown between concurrency levels (lets the server settle).</summary>
    public int CooldownSeconds { get; set; } = 5;

    /// <summary>Correctness mode: stop at the first FAIL/ERROR (default) or keep going and report everything.</summary>
    public bool StopOnFirstFailure { get; set; } = true;

    /// <summary>Correctness mode: run only scenarios whose "Feature/Name" contains this text (case-insensitive).</summary>
    public string ScenarioFilter { get; set; } = "";

    /// <summary>Output directory for English reports (Markdown + JSON).</summary>
    public string OutputDir { get; set; } = "artifacts/live-test";

    /// <summary>Read-only SSH monitor of the server's hardware during load.</summary>
    public SshMonitorOptions Ssh { get; set; } = new();

    public sealed class SshMonitorOptions
    {
        /// <summary>When false, capacity runs report client-side metrics + /health/ready only.</summary>
        public bool Enabled { get; set; }

        /// <summary>SSH host (usually the same server host).</summary>
        public string Host { get; set; } = "188.212.99.234";

        /// <summary>SSH port.</summary>
        public int Port { get; set; } = 22;

        /// <summary>SSH user with read access to free/ps/ss and (sudo) pg_stat_activity.</summary>
        public string User { get; set; } = "";

        /// <summary>Path to the private key file (never the key material itself).</summary>
        public string KeyPath { get; set; } = "";

        /// <summary>systemd unit name of the server process (for is-active + RSS).</summary>
        public string ServiceName { get; set; } = "animora";

        /// <summary>Sampling interval milliseconds.</summary>
        public int SampleIntervalMs { get; set; } = 1000;
    }

    /// <summary>
    /// Resolve options from livetest.json (if present next to the exe or CWD) then overlay
    /// environment variables. Env keys use the ANIMORA_LT_ prefix.
    /// </summary>
    public static LiveTestOptions Resolve()
    {
        var o = new LiveTestOptions();

        foreach (var candidate in new[] { "livetest.json", Path.Combine(AppContext.BaseDirectory, "livetest.json") })
        {
            if (File.Exists(candidate))
            {
                try
                {
                    var fromFile = JsonSerializer.Deserialize<LiveTestOptions>(
                        File.ReadAllText(candidate),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (fromFile is not null) { o = fromFile; break; }
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"warning: could not parse {candidate}: {ex.Message}");
                }
            }
        }

        o.ServerBaseUrl = Env("ANIMORA_LT_SERVER_URL", o.ServerBaseUrl);
        o.TenantCode = Env("ANIMORA_LT_TENANT", o.TenantCode);
        o.Username = Env("ANIMORA_LT_USER", o.Username);
        o.Password = Env("ANIMORA_LT_PASSWORD", o.Password);
        o.OutputDir = Env("ANIMORA_LT_OUTPUT", o.OutputDir);
        o.ScenarioFilter = Env("ANIMORA_LT_FILTER", o.ScenarioFilter);
        if (Env("ANIMORA_LT_KEEP_GOING", "") is "1" or "true" or "yes") o.StopOnFirstFailure = false;

        o.ProfileFilter = Env("ANIMORA_LT_PROFILES", o.ProfileFilter);
        if (Env("ANIMORA_LT_ALLOW_ANY_TENANT", "") is "1" or "true" or "yes") o.AllowAnyTenant = true;
        if (int.TryParse(Env("ANIMORA_LT_MAX_DEVICES", ""), out var devices) && devices > 0) o.MaxDevices = devices;
        if (double.TryParse(Env("ANIMORA_LT_P95_LIMIT_MS", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p95) && p95 > 0) o.P95LimitMs = p95;
        if (int.TryParse(Env("ANIMORA_LT_REQUESTS", ""), out var reqs) && reqs > 0) o.RequestsPerLevel = reqs;

        // Auto-ramp + realistic pacing.
        if (Env("ANIMORA_LT_AUTO_RAMP", "") is "1" or "true" or "yes") o.AutoRamp = true;
        if (int.TryParse(Env("ANIMORA_LT_START_LEVEL", ""), out var start) && start > 0) o.RampStart = start;
        if (int.TryParse(Env("ANIMORA_LT_STEP", ""), out var step) && step > 0) o.RampStep = step;
        if (int.TryParse(Env("ANIMORA_LT_MAX_LEVEL", ""), out var maxLevel) && maxLevel > 0) o.RampMax = maxLevel;
        if (int.TryParse(Env("ANIMORA_LT_BREAK_CONFIRM", ""), out var confirm) && confirm > 0) o.BreakConfirmations = confirm;
        if (int.TryParse(Env("ANIMORA_LT_DURATION", ""), out var dur) && dur >= 0) o.LevelDurationSeconds = dur;
        if (int.TryParse(Env("ANIMORA_LT_THINK_MIN_MS", ""), out var tmin) && tmin >= 0) o.ThinkTimeMinMs = tmin;
        if (int.TryParse(Env("ANIMORA_LT_THINK_MAX_MS", ""), out var tmax) && tmax >= 0) o.ThinkTimeMaxMs = tmax;
        if (int.TryParse(Env("ANIMORA_LT_AMBIENT_POLL_SECONDS", ""), out var poll) && poll >= 0) o.AmbientPollSeconds = poll;
        if (int.TryParse(Env("ANIMORA_LT_COOLDOWN", ""), out var cooldown) && cooldown >= 0) o.CooldownSeconds = cooldown;

        var levels = Env("ANIMORA_LT_LEVELS", "");
        if (!string.IsNullOrWhiteSpace(levels))
        {
            var parsed = levels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var n) ? n : -1).Where(n => n > 0).ToArray();
            if (parsed.Length > 0) o.ConcurrencyLevels = parsed;
        }

        o.Ssh.Enabled = Env("ANIMORA_LT_SSH", o.Ssh.Enabled ? "1" : "0") is "1" or "true" or "yes";
        o.Ssh.Host = Env("ANIMORA_LT_SSH_HOST", o.Ssh.Host);
        o.Ssh.User = Env("ANIMORA_LT_SSH_USER", o.Ssh.User);
        o.Ssh.KeyPath = Env("ANIMORA_LT_SSH_KEY", o.Ssh.KeyPath);
        if (int.TryParse(Env("ANIMORA_LT_SSH_PORT", ""), out var port) && port > 0) o.Ssh.Port = port;

        return o;
    }

    /// <summary>Validate required fields for a given mode; returns human-readable problems (English).</summary>
    public IReadOnlyList<string> Validate(bool requiresAuth)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(ServerBaseUrl) || !Uri.TryCreate(ServerBaseUrl, UriKind.Absolute, out _))
            problems.Add("ServerBaseUrl is missing or not an absolute URL.");
        if (requiresAuth)
        {
            if (string.IsNullOrWhiteSpace(TenantCode)) problems.Add("TenantCode is required (set ANIMORA_LT_TENANT).");
            if (string.IsNullOrWhiteSpace(Username)) problems.Add("Username is required (set ANIMORA_LT_USER).");
            if (string.IsNullOrWhiteSpace(Password)) problems.Add("Password is required (set ANIMORA_LT_PASSWORD).");
        }
        if (Ssh.Enabled)
        {
            if (string.IsNullOrWhiteSpace(Ssh.Host)) problems.Add("Ssh.Host is required when SSH monitor is enabled.");
            if (string.IsNullOrWhiteSpace(Ssh.User)) problems.Add("Ssh.User is required when SSH monitor is enabled.");
        }
        return problems;
    }

    private static string Env(string key, string fallback)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? fallback : v;
    }
}
