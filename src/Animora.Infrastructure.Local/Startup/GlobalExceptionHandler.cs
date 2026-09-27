using System.Text.Json;
using Animora.Application.Auditing;
using Animora.Application.Correlation;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Startup;

public sealed record CrashReport(
    DateTimeOffset OccurredAtUtc,
    string ExceptionType,
    string Message,
    string? StackTrace,
    AmbientCorrelation? Correlation,
    LocalStoreDiagnostics? Store,
    IReadOnlyList<string> LastLogLines);

/// <summary>Owns process-wide exception hooks and emits a bounded, local crash report.</summary>
public sealed class GlobalExceptionHandler(
    IOptions<LocalStoreOptions> options,
    ICorrelationContext correlation,
    IClock clock,
    IServiceScopeFactory scopes,
    IUserNotificationPort notifications,
    ILogger<GlobalExceptionHandler> logger)
{
    public const int FatalExitCode = 70;
    public const string MarkerFileName = "crash.marker";
    private int _registered;

    public LocalStoreDiagnostics? Diagnostics { get; set; }

    public void RegisterProcessHooks()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public bool HandleUiException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Persist(exception);
        notifications.Notify(NotificationSeverity.Error, "CRASH_RECOVERY_REQUIRED", null,
            new NotificationAction("open-diagnostics", "diagnostics"));
        return IsRecoverable(exception);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception) Persist(exception);
        if (args.IsTerminating) Environment.ExitCode = FatalExitCode;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Persist(args.Exception);
        args.SetObserved();
    }

    private void Persist(Exception exception)
    {
        try
        {
            var dataDirectory = options.Value.DataDirectory;
            var crashDirectory = Path.Combine(dataDirectory, "crashes");
            Directory.CreateDirectory(crashDirectory);
            var now = clock.UtcNow;
            var report = new CrashReport(now, exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message, exception.StackTrace, correlation.Current, Diagnostics,
                ReadLastLogLines(Path.Combine(dataDirectory, "logs"), 50));
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            var stamp = now.ToUniversalTime().ToString("yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(crashDirectory, $"crash-{stamp}.json"), json);
            File.WriteAllText(Path.Combine(dataDirectory, MarkerFileName), stamp);
            _ = WriteAuditAsync(exception, now);
            logger.LogCritical(exception, "Unhandled client exception; crash report {CrashStamp}", stamp);
        }
        catch
        {
            // A crash reporter must never recursively crash the process.
        }
    }

    private async Task WriteAuditAsync(Exception exception, DateTimeOffset now)
    {
        try
        {
            var ambient = correlation.Current;
            await using var scope = scopes.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetService<IAuditWriter>();
            if (audit is null) return;
            var operationId = ambient?.OperationId ?? IdGenerator.NewId(now);
            await audit.WriteAsync(new AuditRecord(
                ambient?.TenantId ?? string.Empty,
                ambient?.UserId ?? string.Empty,
                ambient?.DeviceId ?? string.Empty,
                now,
                AuditCategory.Maintenance,
                "client.crash",
                null,
                null,
                operationId,
                ambient?.CorrelationId ?? operationId,
                AuditOutcome.Failed,
                null,
                null,
                null,
                null,
                exception.GetType().Name)).ConfigureAwait(false);
        }
        catch
        {
            // The store can be the failing component; the file report is authoritative fallback.
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    private static IReadOnlyList<string> ReadLastLogLines(string directory, int count)
    {
        try
        {
            var latest = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            return latest is null ? [] : File.ReadLines(latest).TakeLast(count).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
