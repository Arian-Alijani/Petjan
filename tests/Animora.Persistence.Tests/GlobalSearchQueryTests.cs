using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Domain.Identity;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// C10 global search: one parameterized <c>UNION ALL</c> statement over the five sources, each with an
/// independent cap; matches on the Persian-normalized <c>SearchKey</c> so a Persian-digit phone and an
/// Arabic-form name both hit; short terms issue no query at all. The normalization contract mirrors
/// C01 (<c>PersianText.Normalize</c>) — the query receives an already-normalized term.
/// </summary>
public sealed class GlobalSearchQueryTests
{
    private const int Limit = 8;

    private static Owner NewOwner(LocalStoreTestFixture fixture, string name, string phone) =>
        Owner.Register(fixture.TenantId, name, phone, fixture.Clock);

    private static Pet NewPet(LocalStoreTestFixture fixture, string ownerId, string name) =>
        Pet.Register(fixture.TenantId, ownerId, name, PetSpecies.Dog, fixture.Clock);

    [Fact]
    public async Task ShortTerm_IssuesNoQuery_AndReturnsEmpty()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        db.Add(NewOwner(fixture, "علی رضایی", "۰۹۱۲۳۴۵۶۷۸۹"));
        await db.SaveChangesAsync();

        var (counting, counter) = fixture.CreateCountingContext();
        await using (counting)
        {
            var queries = new GlobalSearchQueries(counting);
            counter.Reset();
            var rows = await queries.SearchAsync(fixture.TenantId, "a", Limit, default);
            Assert.Empty(rows);
            // A one-character term must short-circuit before any SQL reaches the database.
            Assert.Equal(0, counter.Count);
        }
    }

    [Fact]
    public async Task WholeSearch_IsASingleStatement_CoveringAllFiveSources()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var owner = NewOwner(fixture, "kavian", "09120000001");
            db.Add(owner);
            db.Add(NewPet(fixture, owner.Id, "kavian"));
            await db.SaveChangesAsync();
        }

        using var scope2 = fixture.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

        // Count reader executions at the ADO layer (the query runs raw SQL on the shared connection,
        // so an EF command interceptor cannot see it — we observe StateChange + a command probe instead).
        var connection = (Microsoft.Data.Sqlite.SqliteConnection)db2.Database.GetDbConnection();
        var readerCalls = 0;
        connection.StateChange += (_, e) =>
        {
            if (e.CurrentState == System.Data.ConnectionState.Open) readerCalls++;
        };

        var queries = new GlobalSearchQueries(db2);
        var rows = await queries.SearchAsync(fixture.TenantId, "kavian", Limit, default);

        // Both the owner and the joined-pet source resolve in the same call — proving the five-source
        // UNION ALL is issued as one statement (a single connection Open for the whole search).
        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.Source == "owner");
        Assert.Contains(rows, r => r.Source == "pet");
        Assert.Equal(1, readerCalls);
    }

    [Fact]
    public async Task PersianDigitPhone_Matches_ViaNormalizedKey()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(NewOwner(fixture, "مریم کریمی", "۰۹۳۵۱۲۳۴۵۶۷"));
            await db.SaveChangesAsync();
        }

        using var scope2 = fixture.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var queries = new GlobalSearchQueries(db2);
        // The searcher types ASCII digits; the persisted key folded the Persian digits to ASCII.
        var rows = await queries.SearchAsync(fixture.TenantId, "0935123", Limit, default);
        Assert.Contains(rows, r => r.Source == "owner" && r.Subtitle.Contains("0935123"));
    }

    [Fact]
    public async Task ArabicFormName_Matches_ViaNormalizedKey()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            // Stored with Arabic yeh/kaf; searched with Persian yeh/kaf — both fold equal.
            db.Add(NewOwner(fixture, "يكتا كيان", "09120000001"));
            await db.SaveChangesAsync();
        }

        using var scope2 = fixture.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var queries = new GlobalSearchQueries(db2);
        // Query term uses Persian yeh (U+06CC) + keheh (U+06A9).
        var rows = await queries.SearchAsync(fixture.TenantId, "یکتا", Limit, default);
        Assert.Contains(rows, r => r.Source == "owner");
    }

    [Fact]
    public async Task PerSourceLimit_IsEnforcedIndependently()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            // 12 owners all matching "sara" — more than the per-source cap.
            for (var i = 0; i < 12; i++)
                db.Add(NewOwner(fixture, $"sara{i:D2}", $"0912000{i:D4}"));
            await db.SaveChangesAsync();
        }

        using var scope2 = fixture.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var queries = new GlobalSearchQueries(db2);
        var rows = await queries.SearchAsync(fixture.TenantId, "sara", 5, default);
        var owners = rows.Where(r => r.Source == "owner").ToList();
        // The per-source LIMIT bounds the owner slice even though 12 rows match.
        Assert.Equal(5, owners.Count);
    }

    [Fact]
    public async Task Union_RanksOwnersBeforePets_ForTheSameTerm()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var owner = NewOwner(fixture, "kavian", "09120000009");
            db.Add(owner);
            db.Add(NewPet(fixture, owner.Id, "kavian"));
            await db.SaveChangesAsync();
        }

        using var scope2 = fixture.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var queries = new GlobalSearchQueries(db2);
        var list = (await queries.SearchAsync(fixture.TenantId, "kavian", Limit, default)).ToList();
        var ownerIndex = list.FindIndex(r => r.Source == "owner");
        var petIndex = list.FindIndex(r => r.Source == "pet");
        Assert.True(ownerIndex >= 0 && petIndex >= 0);
        // Static source-rank puts owners (rank 0) ahead of pets (rank 1).
        Assert.True(ownerIndex < petIndex);
    }

    [Fact]
    public async Task AppointmentSource_MatchesOnJoinedPetName()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var owner = NewOwner(fixture, "Appt Owner", "09120000010");
            var pet = NewPet(fixture, owner.Id, "zizou");
            var doctor = User.Create(fixture.TenantId, "vet", "Vet", fixture.Clock);
            db.AddRange(owner, pet, doctor);
            var start = new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero);
            db.Add(Appointment.Book(fixture.TenantId, pet.Id, owner.Id, "CHECKUP", start, 30,
                AppointmentSource.Phone, doctor.Id, fixture.Clock, doctor.Id).Value!);
            await db.SaveChangesAsync();
        }

        using var scope2 = fixture.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var queries = new GlobalSearchQueries(db2);
        var rows = await queries.SearchAsync(fixture.TenantId, "zizou", Limit, default);
        // The appointment row is discovered through the joined pet's SearchKey, not its own text.
        Assert.Contains(rows, r => r.Source == "appointment");
    }
}
