using Animora.Persistence.PostgreSql.Options;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Insight;
/// <summary>Reporting-only projected model. No business entities, tables or migrations at launch.</summary>
public sealed class AnimoraInsightReadContext(DbContextOptions<AnimoraInsightReadContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
            if (entity.GetTableName() is not { } table || !InsightSourceCatalog.AllowedTables.Contains(table))
                throw new InvalidOperationException("An insight projection is not in the source catalogue.");
    }
    internal async Task BindAsync(ReportingQuerySession session, CancellationToken ct = default)
    {
        Database.SetDbConnection(session.Connection, contextOwnsConnection: false);
        Database.SetCommandTimeout(ReportingQuerySession.TimeoutSeconds);
        await Database.UseTransactionAsync(session.Transaction, ct).ConfigureAwait(false);
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw new InvalidOperationException("Insight is read-only.");
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        => throw new InvalidOperationException("Insight is read-only.");
}
