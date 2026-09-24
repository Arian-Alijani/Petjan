using System.Globalization;
using Animora.Domain.Results;
using Microsoft.Data.Sqlite;

namespace Animora.Infrastructure.Local.Startup;

/// <summary>
/// T-RULE 004: the native SQLite actually loaded is asserted at runtime (a package version is never proof).
/// Failure is fatal for startup and carries the actual version as detail.
/// </summary>
public static class NativeSqliteAssertion
{
    public static readonly Version Minimum = new(3, 50, 2);

    public static Result Assert(SqliteConnection open)
    {
        ArgumentNullException.ThrowIfNull(open);
        using var command = open.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var actual = command.ExecuteScalar() as string ?? string.Empty;
        var parsed = Parse(actual);
        if (parsed.IsFailure) return parsed.ToResult();
        return parsed.Value! < Minimum
            ? Result.Fail(ErrorCodes.NativeSqliteTooOld, actual)
            : Result.Ok();
    }

    /// <summary>Parses <c>major.minor.patch</c>; anything else is <see cref="ErrorCodes.NativeSqliteTooOld"/>.</summary>
    public static Result<Version> Parse(string value)
    {
        var parts = (value ?? string.Empty).Trim().Split('.');
        if (parts.Length != 3) return Result<Version>.Fail(ErrorCodes.NativeSqliteTooOld, value);
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return Result<Version>.Fail(ErrorCodes.NativeSqliteTooOld, value);
        }
        return Result<Version>.Ok(new Version(numbers[0], numbers[1], numbers[2]));
    }
}
