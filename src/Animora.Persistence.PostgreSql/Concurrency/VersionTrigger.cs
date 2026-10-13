using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Concurrency;

/// <summary>
/// Migration helper that attaches/detaches the per-table <c>BEFORE INSERT OR UPDATE</c>
/// trigger which regenerates <c>"Version"</c> and <c>"UpdatedAt"</c> via <c>animora_set_version()</c>.
/// Database ownership covers raw-SQL and child-only updates as well as EF saves. The function is
/// created in <c>101_ServerFoundation</c> and extended by <c>116_SyncTimestamps</c>.
/// </summary>
public static class VersionTrigger
{
    /// <summary>Name of the shared trigger function.</summary>
    public const string FunctionName = "animora_set_version";

    /// <summary>Creates <c>trg_&lt;table&gt;_version</c> on <paramref name="table"/>.</summary>
    public static void Apply(MigrationBuilder migrationBuilder, string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        migrationBuilder.Sql(
            $"CREATE TRIGGER trg_{table}_version BEFORE INSERT OR UPDATE ON \"{table}\" " +
            $"FOR EACH ROW EXECUTE FUNCTION {FunctionName}();");
    }

    /// <summary>Drops <c>trg_&lt;table&gt;_version</c> from <paramref name="table"/>.</summary>
    public static void Drop(MigrationBuilder migrationBuilder, string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{table}_version ON \"{table}\";");
    }
}
