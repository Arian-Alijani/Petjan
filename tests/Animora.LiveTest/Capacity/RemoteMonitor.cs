using System.Diagnostics;
using System.Globalization;

namespace Animora.LiveTest.Capacity;

/// <summary>
/// READ-ONLY sampler of the live server's hardware over ONE long-lived <c>ssh</c> session (system binary, no extra
/// package). A tiny POSIX loop runs remotely and streams one block per interval; it reads /proc, <c>ss</c>,
/// <c>systemctl is-active</c>, <c>ps</c> and (when passwordless sudo allows) <c>pg_stat_activity</c>. It never writes
/// anything on the server. Any monitor failure only ends sampling — it never fails the load run.
/// </summary>
public sealed class RemoteMonitor : IAsyncDisposable
{
    private const string BlockMarker = "@@SAMPLE";

    private readonly Config.LiveTestOptions.SshMonitorOptions _ssh;
    private readonly int _serverPort;
    private readonly object _gate = new();
    private readonly List<ServerSample> _samples = [];
    private readonly CancellationTokenSource _stop = new();
    private Process? _process;
    private Task? _reader;
    private int? _pgMaxConnections;

    public RemoteMonitor(Config.LiveTestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ssh = options.Ssh;
        _serverPort = Uri.TryCreate(options.ServerBaseUrl, UriKind.Absolute, out var uri) ? uri.Port : 5000;
    }

    /// <summary>Why sampling stopped or never started (null while healthy).</summary>
    public string? Problem { get; private set; }

    /// <summary>True once at least one sample arrived.</summary>
    public bool HasData
    {
        get { lock (_gate) return _samples.Count > 0; }
    }

    /// <summary>Server <c>max_connections</c> when readable.</summary>
    public int? PgMaxConnections => _pgMaxConnections;

    /// <summary>Starts the SSH sampler. Returns false (with <see cref="Problem"/> set) when it cannot start.</summary>
    public bool Start()
    {
        if (!_ssh.Enabled) { Problem = "SSH monitor disabled."; return false; }

        var psi = new ProcessStartInfo("ssh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in BuildSshArguments(_ssh)) psi.ArgumentList.Add(arg);

        try
        {
            _process = Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Problem = $"could not start the system 'ssh' binary: {ex.Message}";
            return false;
        }

        if (_process is null) { Problem = "could not start the system 'ssh' binary."; return false; }

        // Script goes over stdin so no remote-side quoting is needed; inner commands read /dev/null, never the script.
        _process.StandardInput.Write(BuildRemoteScript(_ssh, _serverPort));
        _process.StandardInput.Close();
        _reader = Task.Run(() => ReadLoopAsync(_process, _stop.Token));
        _ = Task.Run(() => DrainStderrAsync(_process));
        return true;
    }

    /// <summary>ssh arguments: key auth only (never prompts), short connect timeout, keepalive, no TTY, read-only usage.</summary>
    public static IReadOnlyList<string> BuildSshArguments(Config.LiveTestOptions.SshMonitorOptions ssh)
    {
        var args = new List<string>
        {
            "-o", "BatchMode=yes",
            "-o", "ConnectTimeout=10",
            "-o", "ServerAliveInterval=15",
            "-o", "ServerAliveCountMax=3",
            "-o", "StrictHostKeyChecking=accept-new",
            "-T",
            "-p", ssh.Port.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(ssh.KeyPath))
        {
            args.Add("-i");
            args.Add(ssh.KeyPath);
            args.Add("-o");
            args.Add("IdentitiesOnly=yes");
        }

        args.Add($"{ssh.User}@{ssh.Host}");
        args.Add("sh -s");
        return args;
    }

    /// <summary>The remote sampling loop. Pure reads; every external command has stdin from /dev/null and errors silenced.</summary>
    public static string BuildRemoteScript(Config.LiveTestOptions.SshMonitorOptions ssh, int serverPort)
    {
        var interval = Math.Max(250, ssh.SampleIntervalMs) / 1000.0;
        var service = SanitizeUnit(ssh.ServiceName);
        // The script is fed to `sh -s` on the server: with a CRLF checkout every line would end in \r and dash
        // fails with "end of file unexpected (expecting fi)". Always emit LF regardless of the checkout.
        return ($$"""
            PGMAX=$(sudo -n -u postgres psql -tAc 'show max_connections' </dev/null 2>/dev/null | tr -d ' \r')
            echo "pgmax ${PGMAX:-na}"
            echo "hz $(getconf CLK_TCK </dev/null 2>/dev/null || echo 100)"
            echo "cores $(nproc </dev/null 2>/dev/null || echo 1)"
            while :; do
              echo "{{BlockMarker}}"
              head -n1 /proc/stat </dev/null
              cat /proc/loadavg </dev/null
              grep -E '^(MemTotal|MemAvailable|SwapTotal|SwapFree):' /proc/meminfo </dev/null
              echo "est $(ss -Htn state established '( sport = :{{serverPort}} )' </dev/null 2>/dev/null | wc -l)"
              echo "svc $(systemctl is-active {{service}} </dev/null 2>/dev/null)"
              PID=$(systemctl show -p MainPID --value {{service}} </dev/null 2>/dev/null)
              if [ -n "$PID" ] && [ "$PID" != "0" ]; then echo "rss $(ps -o rss= -p "$PID" </dev/null 2>/dev/null | tr -d ' ')"; echo "pcpu $(awk '{print $14+$15}' /proc/$PID/stat </dev/null 2>/dev/null)"; fi
              sudo -n -u postgres psql -tAF'|' -c "select coalesce(state,'none'), count(*) from pg_stat_activity where datname='animora' group by 1" </dev/null 2>/dev/null | sed 's/^/pg|/'
              echo "@@END"
              sleep {{interval.ToString("0.###", CultureInfo.InvariantCulture)}}
            done

            """).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>Only characters legal in a systemd unit name survive (the value is interpolated into the remote script).</summary>
    public static string SanitizeUnit(string unit)
    {
        var clean = new string(unit.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or '@').ToArray());
        return clean.Length == 0 ? "animora" : clean;
    }

    private async Task ReadLoopAsync(Process process, CancellationToken ct)
    {
        var block = new List<string>();
        var inBlock = false;
        (long Idle, long Total)? previousCpu = null;
        (long Ticks, DateTimeOffset At)? previousService = null;
        var clockTicksPerSecond = 100;
        var cpuCores = 1;
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                if (line.StartsWith("pgmax ", StringComparison.Ordinal))
                {
                    if (int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)) _pgMaxConnections = max;
                }
                else if (line.StartsWith("hz ", StringComparison.Ordinal))
                {
                    if (int.TryParse(line.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hz) && hz > 0)
                        clockTicksPerSecond = hz;
                }
                else if (line.StartsWith("cores ", StringComparison.Ordinal))
                {
                    if (int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cores) && cores > 0)
                        cpuCores = cores;
                }
                else if (line == BlockMarker)
                {
                    block.Clear();
                    inBlock = true;
                }
                else if (line == "@@END" && inBlock)
                {
                    inBlock = false;
                    if (TryParseBlock(block, DateTimeOffset.UtcNow, ref previousCpu, out var sample, ref previousService, clockTicksPerSecond, cpuCores))
                    {
                        lock (_gate) _samples.Add(sample!);
                    }
                }
                else if (inBlock)
                {
                    block.Add(line);
                }
            }

            if (!ct.IsCancellationRequested && Problem is null)
            {
                Problem = _samples.Count == 0
                    ? "ssh monitor produced no samples (check Ssh.User / key / host reachability)."
                    : "ssh monitor session ended early.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Problem = $"ssh monitor stopped: {ex.Message}";
        }
    }

    private async Task DrainStderrAsync(Process process)
    {
        try
        {
            var text = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (first is not null && !HasData) Problem = $"ssh: {first}";
        }
        catch (Exception)
        {
            // best effort diagnostics only
        }
    }

    /// <summary>
    /// Parses one remote block into a sample. Machine CPU % needs the previous <c>/proc/stat</c> reading and the
    /// server-process CPU % needs the previous tick count of that process (both yield null on the first block).
    /// </summary>
    public static bool TryParseBlock(IReadOnlyList<string> lines, DateTimeOffset at, ref (long Idle, long Total)? previousCpu,
        out ServerSample? sample, ref (long Ticks, DateTimeOffset At)? previousServiceTicks, int clockTicksPerSecond = 100,
        int cpuCores = 1)
    {
        sample = null;
        double load1 = 0, memTotalKb = 0, memAvailKb = 0, swapTotalKb = 0, swapFreeKb = 0, cpuPct = 0;
        int established = 0;
        bool? serviceActive = null;
        double? rssKb = null;
        double? serviceCpuPct = null;
        int? pgTotal = null, pgActive = null, pgIdleTx = null;
        var sawCpu = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(p => long.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
                if (parts.Length >= 4)
                {
                    long idle = parts[3] + (parts.Length > 4 ? parts[4] : 0); // idle + iowait
                    long total = parts.Take(8).Sum();                          // user..steal
                    if (previousCpu is { } prev && total > prev.Total)
                    {
                        cpuPct = 100.0 * (1.0 - (double)(idle - prev.Idle) / (total - prev.Total));
                    }

                    previousCpu = (idle, total);
                    sawCpu = true;
                }
            }
            else if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) memTotalKb = KbOf(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) memAvailKb = KbOf(line);
            else if (line.StartsWith("SwapTotal:", StringComparison.Ordinal)) swapTotalKb = KbOf(line);
            else if (line.StartsWith("SwapFree:", StringComparison.Ordinal)) swapFreeKb = KbOf(line);
            else if (line.StartsWith("est ", StringComparison.Ordinal)) _ = int.TryParse(line.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out established);
            else if (line.StartsWith("svc ", StringComparison.Ordinal)) serviceActive = line[4..].Trim() == "active";
            else if (line.StartsWith("rss ", StringComparison.Ordinal) && double.TryParse(line.AsSpan(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var kb)) rssKb = kb;
            else if (line.StartsWith("pcpu ", StringComparison.Ordinal)
                && long.TryParse(line.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var svcTicks))
            {
                // utime+stime ticks of the SERVER process only (excludes the load generator and PostgreSQL).
                if (previousServiceTicks is { } prevSvc)
                {
                    var seconds = (at - prevSvc.At).TotalSeconds;
                    if (seconds > 0.05 && svcTicks >= prevSvc.Ticks)
                    {
                        // Expressed as % of the WHOLE machine (ticks/hz = core-seconds), so it is directly comparable
                        // with the host CPU column instead of being "% of one core".
                        serviceCpuPct = Math.Clamp(100.0 * (svcTicks - prevSvc.Ticks) / (clockTicksPerSecond * seconds)
                                                   / Math.Max(1, cpuCores), 0, 100);
                    }
                }

                previousServiceTicks = (svcTicks, at);
            }
            else if (line.StartsWith("pg|", StringComparison.Ordinal))
            {
                var cols = line.Split('|');
                if (cols.Length >= 3 && int.TryParse(cols[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    pgTotal = (pgTotal ?? 0) + n;
                    if (cols[1] == "active") pgActive = (pgActive ?? 0) + n;
                    if (cols[1].StartsWith("idle in transaction", StringComparison.Ordinal)) pgIdleTx = (pgIdleTx ?? 0) + n;
                }
            }
            else if (line.Length > 0 && char.IsDigit(line[0]) && line.Contains('.') && load1 == 0)
            {
                var first = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                _ = double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out load1);
            }
        }

        if (!sawCpu || memTotalKb <= 0) return false;
        sample = new ServerSample(at, Math.Clamp(cpuPct, 0, 100), load1, memTotalKb / 1024.0, memAvailKb / 1024.0,
            Math.Max(0, swapTotalKb - swapFreeKb) / 1024.0, established, serviceActive, rssKb / 1024.0,
            serviceCpuPct,
            pgTotal, pgActive ?? (pgTotal is null ? null : 0), pgIdleTx ?? (pgTotal is null ? null : 0));
        return true;

        static double KbOf(string line)
        {
            var digits = new string(line.SkipWhile(ch => ch != ':').Skip(1).SkipWhile(char.IsWhiteSpace).TakeWhile(char.IsDigit).ToArray());
            return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
    }

    /// <summary>Copy of every sample so far (ordered by time).</summary>
    public IReadOnlyList<ServerSample> Snapshot()
    {
        lock (_gate) return _samples.ToArray();
    }

    /// <summary>Stops the sampler and kills the ssh child (the remote loop dies with its pipe).</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // already gone
        }

        if (_reader is not null)
        {
            try { await _reader.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (Exception) { /* reader is best effort */ }
        }

        _process?.Dispose();
        _stop.Dispose();
    }
}
