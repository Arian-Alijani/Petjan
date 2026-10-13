using Microsoft.Extensions.Options;

namespace Animora.Persistence.PostgreSql.Options;

/// <summary>
/// Validates the pool budget so a mis-sized configuration fails at boot
/// (<c>ValidateOnStart</c>) instead of exhausting PostgreSQL connections later.
/// </summary>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    /// <summary>Total pooled connections allowed on the 2 GiB profile.</summary>
    public const int Budget2GiB = 12;

    /// <summary>Total pooled connections allowed on the 4 GiB profile.</summary>
    public const int Budget4GiB = 20;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.OltpWriteMax < 1)
        {
            failures.Add("Database:OltpWriteMax must be >= 1.");
        }

        if (options.OltpReadMax < 1)
        {
            failures.Add("Database:OltpReadMax must be >= 1.");
        }

        if (options.KeyRingMax < 1)
        {
            failures.Add("Database:KeyRingMax must be >= 1.");
        }

        if (options.WorkerMax < 1) failures.Add("Database:WorkerMax must be >= 1.");

        if (options.SyncAdmissionMax is < 0 or > 64) failures.Add("Database:SyncAdmissionMax must be 0 (auto) or between 1 and 64.");
        if (options.WorkerAdmissionMax is < 0 or > 16) failures.Add("Database:WorkerAdmissionMax must be 0 (auto) or between 1 and 16.");
        if (options.CommandTimeoutSeconds is < 5 or > 300) failures.Add("Database:CommandTimeoutSeconds must be between 5 and 300.");
        if (options.ConnectionIdleLifetimeSeconds is < 10 or > 3600) failures.Add("Database:ConnectionIdleLifetimeSeconds must be between 10 and 3600.");
        if (options.ConnectionPruningIntervalSeconds is < 1 or > 600) failures.Add("Database:ConnectionPruningIntervalSeconds must be between 1 and 600.");

        if (options.PoolWaitSeconds is < 1 or > 60) failures.Add("Database:PoolWaitSeconds must be between 1 and 60.");

        if (options.ProfileGiB is not (2 or 4))
        {
            failures.Add("Database:ProfileGiB must be 2 or 4.");
        }
        else
        {
            long total = (long)options.OltpWriteMax + options.OltpReadMax + options.KeyRingMax + options.WorkerMax;
            int budget = options.ProfileGiB == 2 ? Budget2GiB : Budget4GiB;
            if (total > budget)
            {
                failures.Add(
                    $"Database pool sizes sum to {total}, exceeding the {budget}-connection budget of the {options.ProfileGiB} GiB profile.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
