using System.Data;
using Animora.Application.UseCases.Search;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>One parameterized UNION ALL statement; each of five sources has an independent cap.</summary>
public sealed class GlobalSearchQueries(AnimoraLocalDbContext db) : IGlobalSearchQueries
{
    public async Task<IReadOnlyList<GlobalSearchRow>> SearchAsync(string tenantId, string term, int limit, CancellationToken ct)
    {
        if (term.Length < 2) return [];
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Source, Id, Title, Subtitle FROM (
                  SELECT * FROM (SELECT 0 AS Rank, 'owner' AS Source, Id, FullName AS Title,
                    PhonePrimary AS Subtitle FROM Owners WHERE TenantId = $tenant AND instr(SearchKey, $term) > 0
                    ORDER BY Id DESC LIMIT $limit)
                  UNION ALL
                  SELECT * FROM (SELECT 1 AS Rank, 'pet' AS Source, Id, Name AS Title,
                    OwnerId AS Subtitle FROM Pets WHERE TenantId = $tenant AND instr(SearchKey, $term) > 0
                    ORDER BY Id DESC LIMIT $limit)
                  UNION ALL
                  SELECT * FROM (SELECT 2 AS Rank, 'invoice' AS Source, Id, InvoiceNumber AS Title,
                    COALESCE(OwnerId, '') AS Subtitle FROM Invoices
                    WHERE TenantId = $tenant AND instr(lower(InvoiceNumber), $term) > 0
                    ORDER BY Id DESC LIMIT $limit)
                  UNION ALL
                  SELECT * FROM (SELECT 3 AS Rank, 'product' AS Source, Id, Name AS Title,
                    Sku AS Subtitle FROM Products WHERE TenantId = $tenant
                    AND (instr(lower(Name), $term) > 0 OR instr(lower(Sku), $term) > 0)
                    ORDER BY Id DESC LIMIT $limit)
                  UNION ALL
                  SELECT * FROM (SELECT 4 AS Rank, 'appointment' AS Source, a.Id, p.Name AS Title,
                    a.PetId AS Subtitle FROM Appointments a INNER JOIN Pets p
                    ON p.Id = a.PetId AND p.TenantId = a.TenantId
                    WHERE a.TenantId = $tenant AND instr(p.SearchKey, $term) > 0
                    ORDER BY a.Id DESC LIMIT $limit)
                ) ORDER BY Rank, Id DESC
                """;
            Add("$tenant", tenantId);
            Add("$term", term);
            Add("$limit", limit);
            var rows = new List<GlobalSearchRow>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(new GlobalSearchRow(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            return rows;

            void Add(string name, object value)
            {
                var p = command.CreateParameter();
                p.ParameterName = name;
                p.Value = value;
                command.Parameters.Add(p);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
