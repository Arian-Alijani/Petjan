using Animora.Contracts.Diagnostics;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Startup;

/// <summary>Validates the store and makes expired durable-work leases reclaimable after a marked crash.</summary>
public sealed class CrashRecoveryService(
    IOptions<LocalStoreOptions> options,
    IServiceScopeFactory scopes,
    IClock clock,
    IUserNotificationPort notifications,
    GlobalExceptionHandler exceptionHandler)
{
    public async Task<Result<LocalStoreDiagnostics?>> RecoverIfRequiredAsync(CancellationToken ct = default)
    {
        var marker = Path.Combine(options.Value.DataDirectory, GlobalExceptionHandler.MarkerFileName);
        if (!File.Exists(marker)) return Result<LocalStoreDiagnostics?>.Ok(null);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var integrity = await db.Database.SqlQueryRaw<string>("PRAGMA quick_check;").ToListAsync(ct)
                .ConfigureAwait(false);
            if (integrity.Count == 0 || integrity.Any(value => !string.Equals(value, "ok", StringComparison.Ordinal)))
                return Result<LocalStoreDiagnostics?>.Fail(ErrorCodes.CrashRecoveryRequired, "PRAGMA quick_check failed.");

            var nowTicks = clock.UtcNow.UtcTicks;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE PendingCommands
                SET ClaimToken = NULL, ClaimedUntilUtc = NULL
                WHERE ClaimToken IS NOT NULL AND ClaimedUntilUtc < {nowTicks};
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE OutboxMessages
                SET ClaimToken = NULL, ClaimedUntilUtc = NULL, Status = {(int)OutboxStatus.Retry}
                WHERE ClaimToken IS NOT NULL AND ClaimedUntilUtc < {nowTicks};
                """, ct).ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            File.Delete(marker);
            var diagnostics = exceptionHandler.Diagnostics is { } current
                ? current with { RecoveredFromCrash = true }
                : new LocalStoreDiagnostics(options.Value.DatabasePath, string.Empty, options.Value.EncryptionEnabled,
                    KeySource.None, string.Empty, true, false,
                    File.Exists(options.Value.DatabasePath) ? new FileInfo(options.Value.DatabasePath).Length : 0, null);
            exceptionHandler.Diagnostics = diagnostics;
            notifications.Notify(NotificationSeverity.Warning, "CRASH_RECOVERY_REQUIRED", null,
                new NotificationAction("export-diagnostics", "diagnostics"));
            return Result<LocalStoreDiagnostics?>.Ok(diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result<LocalStoreDiagnostics?>.Fail(ErrorCodes.CrashRecoveryRequired, exception.Message);
        }
    }
}
