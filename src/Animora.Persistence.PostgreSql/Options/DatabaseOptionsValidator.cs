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
