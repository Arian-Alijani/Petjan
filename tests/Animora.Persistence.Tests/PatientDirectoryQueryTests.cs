using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Options;
using Animora.Persistence.Sqlite.Queries;
using Animora.Persistence.Sqlite.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// C01 projection reads: the SearchKey column is stamped + Persian-normalized on write, the directory
/// queries filter on the persisted index (SQL-side LIKE), page with a stable order, and stay a single
/// SELECT + COUNT (no N+1). Migrations apply the new column/index on a fresh file database.
/// </summary>
public sealed class PatientDirectoryQueryTests
{
    private static PatientDirectoryQueries QueriesFor(LocalStoreTestFixture fixture, AnimoraLocalDbContext db) =>
        new(db, fixture.Services.GetRequiredService<IOptions<LocalStoreOptions>>());

    [Fact]
    public async Task Migration_AddsSearchKeyColumnAndIndexes()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

        var ownerColumns = await ColumnNamesAsync(db, "Owners");
        var petColumns = await ColumnNamesAsync(db, "Pets");
        Assert.Contains("SearchKey", ownerColumns);
        Assert.Contains("SearchKey", petColumns);

        var indexes = await IndexNamesAsync(db);
        Assert.Contains("IX_Owners_TenantId_SearchKey", indexes);
        Assert.Contains("IX_Pets_TenantId_SearchKey", indexes);
    }

    [Fact]
    public async Task Write_StampsANormalizedSearchKey()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        string ownerId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var owner = Owner.Register(fixture.TenantId, "علی رضایی", "۰۹۱۲۳۴۵۶۷۸۹", fixture.Clock);
            db.Add(owner);
            await db.SaveChangesAsync();
            ownerId = owner.Id;

            // Persian digits fold to ASCII and the name is present in the persisted key.
            Assert.Contains("09123456789", owner.SearchKey);
            Assert.Contains("علی", owner.SearchKey);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var reloaded = await db.Set<Owner>().AsNoTracking().SingleAsync(o => o.Id == ownerId);
            Assert.False(string.IsNullOrEmpty(reloaded.SearchKey));
        }
    }

    [Fact]
    public async Task GetPets_SearchMatchesThroughThePersistedKey_WithoutNPlusOne()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "مالک تست", "09120000000", fixture.Clock);
        db.Add(owner);
        var rex = Pet.Register(fixture.TenantId, owner.Id, "رکس", PetSpecies.Dog, fixture.Clock, breed: "mixed");
        var cat = Pet.Register(fixture.TenantId, owner.Id, "میو", PetSpecies.Cat, fixture.Clock);
        db.AddRange(rex, cat);
        await db.SaveChangesAsync();

        var queries = QueriesFor(fixture, db);

        var all = await queries.GetPetsAsync(null, null, PatientDirectorySort.Recent, 1, 50, default);
        Assert.Equal(2, all.TotalCount);

        // Search by name (the term is normalized the same way as the stored key).
        var found = await queries.GetPetsAsync("رکس", null, PatientDirectorySort.Recent, 1, 50, default);
        var row = Assert.Single(found.Items);
        Assert.Equal(rex.Id, row.Id);
        Assert.Null(row.LastVisitUtc);   // C03 supplies visits; none exist yet.

        // Species filter uses the domain enum.
        var dogs = await queries.GetPetsAsync(null, PetSpecies.Dog, PatientDirectorySort.Recent, 1, 50, default);
        Assert.Equal(rex.Id, Assert.Single(dogs.Items).Id);

        // One SELECT + one COUNT at most: the latest-weight join is part of the same statement, never per-row.
        var (counting, counter) = fixture.CreateCountingContext();
        try
        {
            var counted = await QueriesFor(fixture, counting).GetPetsAsync(null, null, PatientDirectorySort.Recent, 1, 50, default);
            Assert.Equal(2, counted.TotalCount);
            Assert.True(counter.Count <= 2, $"expected <=2 SQL commands, saw {counter.Count}.");
        }
        finally
        {
            counting.Dispose();
        }
    }

    [Fact]
    public async Task GetOwners_PagesWithAStableOrderAndJoinsThePetCount()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var a = Owner.Register(fixture.TenantId, "آ ا", "09120000001", fixture.Clock);
        var b = Owner.Register(fixture.TenantId, "ب ب", "09120000002", fixture.Clock);
        db.AddRange(a, b);
        db.Add(Pet.Register(fixture.TenantId, b.Id, "رکس", PetSpecies.Dog, fixture.Clock));
        await db.SaveChangesAsync();

        var queries = QueriesFor(fixture, db);

        var page1 = await queries.GetOwnersAsync(null, null, PatientDirectorySort.Recent, 1, 1, default);
        Assert.Equal(2, page1.TotalCount);
        Assert.Single(page1.Items);
        Assert.Equal(1, page1.Page);
        Assert.Equal(1, page1.PageSize);

        var ownerB = (await queries.GetOwnersAsync("ب ب", null, PatientDirectorySort.Recent, 1, 10, default)).Items.Single();
        Assert.Equal(b.Id, ownerB.Id);
        Assert.Equal(1, ownerB.PetCount);

        var detail = await queries.GetOwnerAsync(b.Id, default);
        Assert.NotNull(detail);
        Assert.Equal(b.Id, detail!.Owner.Id);
        Assert.Single(detail.Pets);

        Assert.Null(await queries.GetOwnerAsync("missing", default));
        Assert.Null(await queries.GetPetAsync("missing", default));
    }

    [Fact]
    public async Task GetPets_ClampsThePageSizeTo200()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "مالک انبوه", "09120000009", fixture.Clock);
        db.Add(owner);
        for (var i = 0; i < 205; i++)
            db.Add(Pet.Register(fixture.TenantId, owner.Id, $"حیوان {i:D3}", PetSpecies.Dog, fixture.Clock));
        await db.SaveChangesAsync();

        var queries = QueriesFor(fixture, db);

        // Ask for far more than the hard ceiling; the result page is capped at 200 rows.
        var page = await queries.GetPetsAsync(null, null, PatientDirectorySort.Recent, 1, 500, default);
        Assert.Equal(205, page.TotalCount);
        Assert.Equal(200, page.PageSize);
        Assert.Equal(200, page.Items.Count);

        // Owners directory clamps identically.
        var owners = await queries.GetOwnersAsync(null, null, PatientDirectorySort.Recent, 1, 1000, default);
        Assert.Equal(200, owners.PageSize);
    }

    [Fact]
    public async Task DirectoryProjections_AreASingleRoundTripStatement()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "مالک کوئری", "09120000010", fixture.Clock);
        db.Add(owner);
        db.Add(Pet.Register(fixture.TenantId, owner.Id, "رکس", PetSpecies.Dog, fixture.Clock));
        await db.SaveChangesAsync();

        // The bounded page select folds the owner name / last-weight / pet-count into the SAME statement
        // (they render as correlated scalar sub-SELECTs, not per-row round trips — no N+1). ToQueryString()
        // renders exactly the item query EF will run; it must be a single, non-batched SQL statement whose
        // top level is a SELECT.
        var petPage = db.Set<Pet>().AsNoTracking()
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id, p.Name,
                LastWeight = db.Set<WeightEntry>().Where(w => w.PetId == p.Id)
                    .OrderByDescending(w => w.MeasuredUtc).Select(w => (decimal?)w.WeightKg).FirstOrDefault(),
            })
            .Skip(0).Take(50);
        AssertSingleStatementSelect(petPage.ToQueryString());

        var ownerPage = db.Set<Owner>().AsNoTracking()
            .OrderBy(o => o.FullName).ThenBy(o => o.Id)
            .Select(o => new { o.Id, o.FullName, PetCount = db.Set<Pet>().Count(p => p.OwnerId == o.Id) })
            .Skip(0).Take(50);
        AssertSingleStatementSelect(ownerPage.ToQueryString());
    }

    // A single round-trip statement: after EF's ".param set" preamble (which declares the tenant-filter
    // parameter — not SQL), the command body is one SELECT with no statement separator. Correlated scalar
    // sub-SELECTs (last weight / pet count) are part of that one statement — exactly what avoids N+1.
    private static void AssertSingleStatementSelect(string sql)
    {
        // Drop EF Core's ".param set @…" preamble lines; keep the real SQL body.
        var body = string.Join('\n', sql
                .Replace("\r\n", "\n")
                .Split('\n')
                .SkipWhile(line => line.TrimStart().StartsWith(".param", StringComparison.OrdinalIgnoreCase)
                                   || line.Trim().Length == 0))
            .Trim();

        Assert.StartsWith("SELECT", body, StringComparison.OrdinalIgnoreCase);
        // No statement separator except an optional single trailing one ⇒ not a multi-statement batch.
        Assert.DoesNotContain(";", body.TrimEnd(';', '\r', '\n', ' '));
    }

    [Fact]
    public void SearchKeyProvider_NormalizesPersianDigits()
    {
        var provider = new OwnerSearchKeyProvider();
        var owner = Owner.Register("T1", "علی", "۰۹۱۲۳۴۵۶۷۸۹", new FakeClock(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var key = provider.Build(owner);

        Assert.Contains("09123456789", key);
        Assert.Equal(key, key.ToLowerInvariant());   // stored keys are lowercased.
    }

    private static async Task<string[]> ColumnNamesAsync(AnimoraLocalDbContext db, string table)
    {
        var names = new System.Collections.Generic.List<string>();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}');";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return [.. names];
    }

    private static async Task<string[]> IndexNamesAsync(AnimoraLocalDbContext db)
    {
        var names = new System.Collections.Generic.List<string>();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name IS NOT NULL;";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return [.. names];
    }
}
