using System.Reflection;
using Animora.Persistence.PostgreSql;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Animora.Server.Tests.Persistence;

/// <summary>
/// Non-gated guard (no database required) that catches drift between
/// <see cref="ServerSchemaVersion.ExpectedHead"/> and the newest migration declared in the
/// persistence assembly. Without this guard the constant silently fell behind the real head
/// (115_Files vs. 117_VisitRadiologyLab), which left <c>/health/ready</c>, <c>/health/startup</c>
/// and the license-key bootstrap permanently not-ready on a correctly-migrated server.
/// </summary>
public sealed class SchemaVersionHeadTests
{
    private static string NewestMigrationId()
    {
        // Enumerate the [Migration("<timestamp>_<name>")] attributes for the server DbContext.
        // Migration ids sort lexicographically by their timestamp prefix, so the max is the head.
        string? head = typeof(AnimoraServerDbContext).Assembly
            .GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => (migration: t, attr: t.GetCustomAttribute<MigrationAttribute>()))
            .Where(x => x.attr is not null
                && x.migration.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(AnimoraServerDbContext))
            .Select(x => x.attr!.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .LastOrDefault();

        Assert.False(string.IsNullOrEmpty(head), "No migrations were found for AnimoraServerDbContext.");
        return head!;
    }

    [Fact]
    public void ExpectedHead_MatchesNewestDeclaredMigration()
    {
        string newest = NewestMigrationId();

        Assert.True(
            ServerSchemaVersion.IsExpectedHead(newest),
            $"ServerSchemaVersion.ExpectedHead ('{ServerSchemaVersion.ExpectedHead}') does not match the newest " +
            $"declared migration ('{newest}'). Bump ServerSchemaVersion.ExpectedHead (and its test assertions) " +
            "whenever a new migration becomes the head, or /health/ready, /health/startup and the license-key " +
            "bootstrap stay permanently not-ready on a correctly-migrated server.");
    }
}
