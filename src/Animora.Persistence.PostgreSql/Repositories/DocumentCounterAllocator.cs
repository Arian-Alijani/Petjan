using System.Globalization;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class DocumentCounterAllocator(AnimoraServerDbContext db)
{
    public async Task<long> NextAsync(string tenantId, string kind, string period, CancellationToken ct)
    {
        if (!string.Equals(tenantId, db.CurrentTenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Counter tenant must match the authenticated tenant.");
        var transaction = db.Database.CurrentTransaction ??
            throw new InvalidOperationException("Document numbers require the command transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO public."DocumentCounters" ("TenantId", "Kind", "Period", "Next")
            VALUES (@tenant, @kind, @period, 1)
            ON CONFLICT ("TenantId", "Kind", "Period") DO UPDATE
            SET "Next" = "DocumentCounters"."Next" + 1 RETURNING "Next";
            """;
        foreach (var (name, value) in new[] { ("tenant", tenantId), ("kind", kind), ("period", period) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    internal static string Year(DateTimeOffset instant) =>
        new PersianCalendar().GetYear(instant.UtcDateTime).ToString("D4", CultureInfo.InvariantCulture);
}
