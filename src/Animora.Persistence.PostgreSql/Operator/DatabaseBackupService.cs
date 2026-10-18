using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Animora.Domain.Results;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>One finished backup file on disk.</summary>
public sealed record DatabaseBackupFile(string FileName, long SizeBytes, DateTimeOffset CreatedUtc);

/// <summary>
/// Full logical backup of the server database through <c>pg_dump --format=custom</c> (compressed, restorable with
/// <c>pg_restore</c> — see docs/release/03-backup-restore-hostmove.md).
/// Security: the executable and every argument are fixed/config-derived (never operator input); the connection secret is
/// handed to the child through <c>PG*</c> environment variables, never the command line, and pg_dump's stderr is never
/// returned to the caller (it can echo connection details). Only one dump runs at a time per process.
/// If <c>pg_dump</c> is not installed on the server the call fails with <see cref="ErrorCodes.StoreUnavailable"/>.
/// </summary>
public static partial class DatabaseBackupService
{
    /// <summary>File names this service produces (and the only names the download endpoint accepts).</summary>
    [GeneratedRegex(@"^animora-\d{8}T\d{6}Z-[0-9a-f]{8}\.dump$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    /// <summary>How many backup files are kept; older ones are deleted after a successful dump.</summary>
    public const int RetainCount = 5;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>True when <paramref name="fileName"/> is exactly a name this service generates (no path separators possible).</summary>
    public static bool IsBackupFileName(string? fileName) => fileName is { Length: <= 64 } && FileNamePattern().IsMatch(fileName);

    /// <summary>Resolves a validated backup file name to its full path inside <paramref name="directory"/>, or null.</summary>
    public static string? Resolve(string directory, string? fileName)
    {
        if (!IsBackupFileName(fileName)) return null;
        var path = Path.Combine(Path.GetFullPath(directory), fileName!);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Newest-first list of the backups currently kept in <paramref name="directory"/>.</summary>
    public static IReadOnlyList<DatabaseBackupFile> List(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return new DirectoryInfo(directory).EnumerateFiles("animora-*.dump")
            .Where(f => IsBackupFileName(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new DatabaseBackupFile(f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero))).ToArray();
    }

    /// <summary>Runs pg_dump into <paramref name="directory"/> and returns the new file, or a typed failure.</summary>
    public static async Task<Result<DatabaseBackupFile>> CreateAsync(string connectionString, string directory, string pgDumpPath,
        DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(pgDumpPath) || string.IsNullOrWhiteSpace(directory))
            return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable);
        NpgsqlConnectionStringBuilder cs;
        try { cs = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable); }
        if (string.IsNullOrWhiteSpace(cs.Host) || string.IsNullOrWhiteSpace(cs.Database))
            return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable);

        if (!await Gate.WaitAsync(0, ct).ConfigureAwait(false)) return Result<DatabaseBackupFile>.Fail(ErrorCodes.ConcurrencyConflict);
        string? path = null;
        try
        {
            var full = Path.GetFullPath(directory);
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(full);
            else Directory.CreateDirectory(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var name = string.Create(CultureInfo.InvariantCulture, $"animora-{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid().ToString("N")[..8]}.dump");
            path = Path.Combine(full, name);

            var psi = new ProcessStartInfo(pgDumpPath)
            {
                RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--no-owner"); psi.ArgumentList.Add("--format=custom"); psi.ArgumentList.Add("--file=" + path);
            // Connection details travel as environment variables so the password never shows up in a process listing.
            psi.Environment["PGHOST"] = cs.Host.Split(',')[0].Trim();
            psi.Environment["PGPORT"] = cs.Port.ToString(CultureInfo.InvariantCulture);
            psi.Environment["PGDATABASE"] = cs.Database!;
            if (!string.IsNullOrEmpty(cs.Username)) psi.Environment["PGUSER"] = cs.Username;
            if (!string.IsNullOrEmpty(cs.Password)) psi.Environment["PGPASSWORD"] = cs.Password;
            psi.Environment["PGSSLMODE"] = cs.SslMode switch
            {
                SslMode.Disable => "disable", SslMode.Allow => "allow", SslMode.Require => "require",
                SslMode.VerifyCA => "verify-ca", SslMode.VerifyFull => "verify-full", _ => "prefer",
            };

            using var process = new Process { StartInfo = psi };
            try { process.Start(); }
            catch (Win32Exception) { return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable); } // pg_dump not installed
            catch (InvalidOperationException) { return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable); }

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(Timeout);
            // Drain both pipes so a chatty dump can never block on a full pipe buffer; the text is intentionally discarded.
            var drain = Task.WhenAll(process.StandardError.ReadToEndAsync(budget.Token), process.StandardOutput.ReadToEndAsync(budget.Token));
            try
            {
                await process.WaitForExitAsync(budget.Token).ConfigureAwait(false);
                await drain.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                TryDelete(path);
                if (ct.IsCancellationRequested) throw;
                return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable); // timed out
            }

            var info = new FileInfo(path);
            if (process.ExitCode != 0 || !info.Exists || info.Length == 0)
            {
                TryDelete(path);
                return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable);
            }
            Prune(full);
            return Result<DatabaseBackupFile>.Ok(new(name, info.Length, now));
        }
        catch (IOException) { if (path is not null) TryDelete(path); return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable); }
        catch (UnauthorizedAccessException) { return Result<DatabaseBackupFile>.Fail(ErrorCodes.StoreUnavailable); }
        finally { Gate.Release(); }
    }

    private static void Prune(string directory)
    {
        foreach (var stale in List(directory).Skip(RetainCount)) TryDelete(Path.Combine(directory, stale.FileName));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
