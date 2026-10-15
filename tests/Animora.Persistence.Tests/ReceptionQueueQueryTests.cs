using System.Data.Common;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

public sealed class ReceptionQueueQueryTests
{
    [Fact]
    public async Task Queue_DerivesAppointmentAndWalkInOnce_WithHalfOpenUtcDay()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "Owner", "09120000000", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "Pet", PetSpecies.Dog, fixture.Clock);
        var start = fixture.Clock.UtcNow;
        var appointment = Appointment.Book(fixture.TenantId, pet.Id, owner.Id, "CHECKUP", start,
            30, AppointmentSource.Phone, "U-1", fixture.Clock).Value!;
        db.AddRange(owner, pet, appointment);
        await db.SaveChangesAsync();

        var queries = new ReceptionQueueQueries(db);
        var day = new DateTimeOffset(start.UtcDateTime.Date, TimeSpan.Zero);
        var initial = await queries.GetQueueAsync(day, day.AddDays(1), default);
        Assert.Equal("Waiting", Assert.Single(initial).State);

        var row = ReceptionQueueEntry.Create(fixture.TenantId, pet.Id, owner.Id, QueueSource.Appointment,
            1, fixture.Clock, appointment.Id);
        var walkIn = ReceptionQueueEntry.Create(fixture.TenantId, pet.Id, owner.Id, QueueSource.WalkIn,
            2, fixture.Clock);
        db.AddRange(row, walkIn);
        await db.SaveChangesAsync();
        var (counted, counter) = fixture.CreateCountingContext();
        using (counted)
        {
            var tickets = await new ReceptionQueueQueries(counted).GetQueueAsync(day, day.AddDays(1), default);
            Assert.Equal(2, tickets.Count);
            Assert.Equal(1, tickets.Count(t => t.TicketId == "apt:" + appointment.Id));
            Assert.Contains(tickets, t => t.TicketId == "wlk:" + walkIn.Id);
            Assert.Equal(1, counter.Count); // one UNION ALL statement; no per-row lookups
        }
        Assert.Empty(await queries.GetQueueAsync(day.AddDays(1), day.AddDays(2), default));
    }

    [Fact]
    public async Task DoctorRail_ComputesBookedCountWithoutPerDoctorQueries()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var doctor = User.Create(fixture.TenantId, "doctor-sample", "Doctor", fixture.Clock);
        db.Add(doctor);
        db.Add(StaffMembership.Create(fixture.TenantId, doctor.Id, StaffRole.Doctor, fixture.Clock));
        var owner = Owner.Register(fixture.TenantId, "Owner", "09120000001", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "Pet", PetSpecies.Cat, fixture.Clock);
        db.AddRange(owner, pet);
        db.Add(Appointment.Book(fixture.TenantId, pet.Id, owner.Id, "CHECKUP", fixture.Clock.UtcNow,
            30, AppointmentSource.Phone, doctor.Id, fixture.Clock, doctor.Id).Value!);
        await db.SaveChangesAsync();
        var day = new DateTimeOffset(fixture.Clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var (counted, counter) = fixture.CreateCountingContext();
        using (counted)
        {
            var rail = await new TriageQueries(counted).GetDoctorRailAsync(day, day.AddDays(1), default);
            Assert.Equal(1, Assert.Single(rail).BookedCount);
            Assert.Equal(1, counter.Count);
        }
    }

    [Fact]
    public async Task DoctorRail_SurfacesTheServerUserId_OfAServerMirroredDoctor()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        const string serverUserId = "01M4X2FVSERVERDOCTOR00000";
        var doctor = User.CreateWithId(serverUserId, fixture.TenantId, "dr.sara", "Dr Sara", fixture.Clock);
        db.Add(doctor);
        db.Add(StaffMembership.Create(fixture.TenantId, doctor.Id, StaffRole.Doctor, fixture.Clock));
        await db.SaveChangesAsync();

        var day = new DateTimeOffset(fixture.Clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var rail = await new TriageQueries(db).GetDoctorRailAsync(day, day.AddDays(1), default);

        Assert.Equal(serverUserId, Assert.Single(rail).Id);
    }

    [Fact]
    public async Task Queue_IsOneUnionAllSelect_FilteredByUtcRange_WithoutDateFunctions()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var capture = new SqlCapture();
        var options = new DbContextOptionsBuilder<AnimoraLocalDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}")
            .AddInterceptors(capture)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        using var db = new AnimoraLocalDbContext(options, new FixedTenantContext(fixture.TenantId), fixture.Clock);
        var day = new DateTimeOffset(fixture.Clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);

        await new ReceptionQueueQueries(db).GetQueueAsync(day, day.AddDays(1), default);

        var sql = Assert.Single(capture.Statements);
        Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(";", sql.TrimEnd(';', '\r', '\n', ' '));
        // The day is a half-open range compared against the stored UTC value — never a SQL date function.
        foreach (var dateFunction in new[] { "date(", "datetime(", "strftime(", "julianday(" })
        {
            Assert.DoesNotContain(dateFunction, sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Records the text of every reader command EF executes.</summary>
    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
