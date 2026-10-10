namespace Animora.Persistence.PostgreSql;

/// <summary>
/// The server schema head this build expects. The runtime NEVER migrates; the readiness
/// <c>schema</c> probe only compares the applied head against this value.
/// </summary>
public static class ServerSchemaVersion
{
    /// <summary>Expected head migration name (without EF's timestamp prefix).</summary>
    public const string ExpectedHead = "111_ServerOutbox";

    /// <summary>
    /// <c>true</c> when <paramref name="appliedMigrationId"/> (EF id <c>&lt;timestamp&gt;_&lt;name&gt;</c>)
    /// is the expected head migration.
    /// </summary>
    public static bool IsExpectedHead(string? appliedMigrationId) =>
        appliedMigrationId is not null
        && (string.Equals(appliedMigrationId, ExpectedHead, StringComparison.Ordinal)
            || appliedMigrationId.EndsWith("_" + ExpectedHead, StringComparison.Ordinal));
}
