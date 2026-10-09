using Npgsql;
using Xunit;

namespace Animora.Server.Tests.Persistence;

/// <summary>
/// Environment contract for PostgreSQL-backed tests. <c>ANIMORA_PG_TEST_CONNECTION</c> points at
/// a server whose role may create databases; every fixture instance creates and drops its own
/// uniquely named database. When it is unset the tests marked <see cref="PgFactAttribute"/> are
/// skipped with a reason — unless <c>ANIMORA_REQUIRE_PG=1</c>, in which case they fail.
/// </summary>
public sealed class PgFixture : IAsyncLifetime
{
    public const string ConnectionEnvVar = "ANIMORA_PG_TEST_CONNECTION";
    public const string RequireEnvVar = "ANIMORA_REQUIRE_PG";

    private string? _adminConnection;
    private string? _databaseName;

    public static string? AdminConnectionFromEnv => Environment.GetEnvironmentVariable(ConnectionEnvVar) is { Length: > 0 } v ? v : null;

    public static bool Required => Environment.GetEnvironmentVariable(RequireEnvVar) == "1";

    /// <summary>Connection string of this run's private database (valid after initialization).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _adminConnection = AdminConnectionFromEnv;
        if (_adminConnection is null)
        {
            if (Required)
            {
                throw new InvalidOperationException($"{RequireEnvVar}=1 but {ConnectionEnvVar} is not set.");
            }

            return;
        }

        _databaseName = "animora_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName, Pooling = false }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_adminConnection is null || _databaseName is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary><c>[Fact]</c> that is skipped (with a reason) when no PostgreSQL test server is configured.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PgFactAttribute : FactAttribute
{
    public PgFactAttribute()
    {
        if (PgFixture.AdminConnectionFromEnv is null && !PgFixture.Required)
        {
            Skip = $"{PgFixture.ConnectionEnvVar} is not set (PostgreSQL-gated test).";
        }
    }
}
