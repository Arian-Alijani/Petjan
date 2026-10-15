using Animora.Application.Abstractions;
using Animora.Application.Activity;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Identity;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Visits;
using Animora.Domain.Boarding;
using Animora.Domain.Cashier;
using Animora.Domain.Clinical;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Seed;
using Animora.Persistence.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests.Scenarios;

/// <summary>One clinic day over a migrated file DB, the real dispatcher/security/journal/UoW,
/// and a single controlled clock. No network or test repository stands in for the product.</summary>
public sealed class OfflineClinicDayScenarioTests
{
    [Fact]
    public async Task ClinicDay_PersistsAllFourteenSteps_AndBoardingConsequences()
    {
        using var fixture = new LocalStoreTestFixture(
            configureOptions: o => { o.AllowInsecureKeyFallback = true; o.SeedSampleData = false; },
            configureServices: s => s.AddAnimoraApplication(b => b.AddIdentityUseCases()
                .AddPatientsUseCases().AddSchedulingUseCases().AddReceptionUseCases()
                .AddVisitsUseCases().AddBoardingUseCases().AddSalesUseCases().AddCashierUseCases()));
        await fixture.MigrateAsync();
        using (var seedScope = fixture.CreateScope())
            { var seed = await seedScope.ServiceProvider.GetRequiredService<LocalSeeder>().SeedAsync(); Assert.True(seed.IsSuccess, $"{seed.Error.Code}: {seed.Detail}"); }

        string? userId = null;
        var operations = new List<string>();
        async Task<T> Send<T>(ICommand<T> command)
        {
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
            using var scope = fixture.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
            var context = identity.BuildAnonymousCommandContext().Value!;
            // Sign-in must stay anonymous, just like AuthenticationService: the previous
            // session can be expired and must not be validated before credentials are checked.
            if (userId is not null && command is not SignInCommand)
                context = context with { UserId = userId };
            var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context);
            Assert.True(result.IsSuccess, $"{command.GetType().Name}: {result.Error.Code}: {result.Detail}");
            if (command is IJournaledCommand) operations.Add(context.OperationId);
            return result.Value!;
        }
        async Task<T> Query<T>(IQuery<T> query)
        {
            using var scope = fixture.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildAnonymousQueryContext()
                with { UserId = userId! };
            var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context);
            Assert.True(result.IsSuccess, result.Error.Code);
            return result.Value!;
        }

        // 1. Real first run creates the owner/admin, device, signed capability and session.
        userId = (await Send(new CompleteFirstRunSetupCommand("Clinic", "Administrator", "admin", "Secret123"))).UserId;
        string doctorId, productId, variantId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Single(await db.Set<Tenant>().ToListAsync());
            Assert.Equal(StaffRole.Manager, (await db.Set<StaffMembership>().SingleAsync()).Role);
            var doctor = User.Create(fixture.TenantId, "doctor", "Doctor", fixture.Clock);
            var membership = StaffMembership.Create(fixture.TenantId, doctor.Id, StaffRole.Doctor, fixture.Clock);
            membership.SetWorkingHours(0, 1440, 127);
            db.AddRange(doctor, membership);
            doctorId = doctor.Id;
            variantId = IdGenerator.NewId(fixture.Clock.Now);
            var category = await db.Set<ProductCategory>().FirstAsync(c => c.Code == "MED");
            var variant = new ProductVariant(variantId, "MED-1-V", ProductVariant.DefaultAttributeName,
                "", Money.Irt(40), Money.Irt(100), new Quantity(0, "PCS"), null, true);
            var product = Product.Create(fixture.TenantId, "MED-1", "Medicine", category.Id,
                ProductKind.Medicine, "PCS", Money.Irt(40), Money.Irt(100), [variant]).Value!;
            productId = product.Id;
            db.Add(product);
            db.Add(StockMovement.Create(fixture.TenantId, productId, variantId, MovementKind.Receipt,
                new Quantity(20, "PCS"), Money.Irt(40), fixture.Clock.Now, userId).Value!);
            // Reference stock fixture, not a clinic mutation: no UoW events or journal rows.
            await db.SaveChangesAsync();
        }

        // 2. Journaled owner and patient registration.
        var ownerId = await Send(new CreateOwnerCommand("Owner", "09120000001", OwnerGender.Unspecified,
            null, null, null, null, null, null, null, null, null, null, null, false));
        var petId = await Send(new CreatePetCommand(ownerId, "Pet", PetSpecies.Dog, null, null,
            PetGender.Unknown, null, null, null, false));

        // 3. Book tomorrow, advance the same clock, then check in.
        var tomorrow = DateOnly.FromDateTime(fixture.Clock.Now.UtcDateTime).AddDays(1);
        var appointmentId = await Send(new BookAppointmentCommand(tomorrow, 720, 30, "CHECKUP",
            doctorId, petId, ownerId, null));
        fixture.Clock.Now = new DateTimeOffset(tomorrow.ToDateTime(new TimeOnly(8, 30)), TimeSpan.Zero);
        // A real session expires overnight; renew through the normal local sign-in use case.
        await Send(new SignInCommand("admin", "Secret123", false));
        await Send(new CheckInAppointmentCommand(appointmentId));

        // 4. Derived reception queue, admit and enter the encounter.
        var ticket = TicketRef.FromAppointment(appointmentId).Format();
        Assert.Contains(await Query(new ReceptionQueueQuery(tomorrow)), r => r.TicketId == ticket);
        await Send(new AdmitTicketCommand(ticket));
        var visitId = (await Send(new StartVisitFromTicketCommand(ticket))).VisitId;
        // StartVisitFromTicket admits/enters atomically; a second EnterVisit would be an invalid transition.

        // 5. Vitals, diagnosis and TWO prescription lines, each one unit of the same medicine.
        await Send(new SaveVisitVitalsCommand(visitId, 12, 38, 90, 20));
        await Send(new SaveVisitNoteCommand(visitId, VisitNoteField.Diagnosis, "DX-01"));
        await Send(new AddPrescriptionLineCommand(visitId, variantId, "Rx-1", VisitLineSource.Product, 100, 1));
        await Send(new AddPrescriptionLineCommand(visitId, variantId, "Rx-2", VisitLineSource.Product, 100, 1));
        await Send(new CompleteVisitCommand(visitId));

        // 6. Ready handoff, invoice and one extra product line (two units).
        var ready = Assert.Single(await Query(new ReadyForBillingQuery()));
        Assert.Equal(petId, ready.PetId);
        var invoiceId = await Send(new CreateInvoiceFromVisitCommand(ready.Id));
        await Send(new AddInvoiceLineCommand(invoiceId, variantId, 2));
        await Send(new PostInvoiceCommand(invoiceId));
        const long total = 400;

        // 7. Cash session and two partial payments.
        var sessionId = await Send(new OpenCashSessionCommand(Money.Irt(50)));
        await Send(new RegisterPaymentCommand(invoiceId, PaymentMethod.Cash, Money.Irt(150)));
        await Send(new RegisterPaymentCommand(invoiceId, PaymentMethod.Cash, Money.Irt(250)));

        var expectedFeed = new[] { ActivityEventKeys.PetRegistered, ActivityEventKeys.AppointmentBooked,
            ActivityEventKeys.VisitCompleted, ActivityEventKeys.InvoicePosted,
            ActivityEventKeys.StockMovementRecorded, ActivityEventKeys.StockMovementRecorded,
            ActivityEventKeys.StockMovementRecorded, ActivityEventKeys.PaymentRegistered, ActivityEventKeys.PaymentRegistered };

        // 8–11. Invoice, payments, exact journal count, stock proof, balanced ledger and NINE ordered events.
        async Task AssertNumbers(AnimoraLocalDbContext db, bool closed)
        {
            var invoice = await db.Set<Invoice>().SingleAsync(i => i.Id == invoiceId);
            Assert.Equal(InvoiceState.Paid, invoice.State);
            Assert.Equal(total, invoice.GrandTotal.Minor);
            var payments = await db.Set<Payment>().Where(p => p.InvoiceId == invoiceId).ToListAsync();
            Assert.Equal(2, payments.Count);
            Assert.Equal(new long[] { 150, 250 }, payments.OrderBy(p => p.ReceivedUtc).Select(p => p.Amount.Minor));
            Assert.All(payments, p => Assert.Equal(sessionId, p.CashSessionId));
            var commands = await db.Set<PendingCommand>().OrderBy(p => p.LocalSequence).ToListAsync();
            Assert.Equal(operations, commands.Select(p => p.OperationId));
            Assert.All(commands, c => Assert.Contains(c.Status, new[] { SettlementState.LocallyPosted, SettlementState.PendingSettlement }));
            Assert.Equal(16_000, (await db.Set<StockOnHandSnapshot>().SingleAsync(s => s.ProductId == productId)).OnHandScaled);
            var movements = await db.Set<StockMovement>().Where(m => m.ProductId == productId).ToListAsync();
            Assert.Equal(16m, movements.Sum(m => m.Quantity.Value));
            Assert.Equal(-4m, movements.Where(m => m.Kind == MovementKind.Sale).Sum(m => m.Quantity.Value));
            var entries = await db.Set<JournalEntry>().ToListAsync();
            Assert.Equal(6, entries.Count); // Invoice + three COGS entries + two payments.
            Assert.All(entries, e => Assert.Equal(0, e.Lines.Sum(l => l.SignedMinor)));
            Assert.Equal(total, entries.SelectMany(e => e.Lines).Where(l => l.AccountCode == AccountCodes.ProductRevenue)
                .Sum(l => -l.SignedMinor));
            Assert.Equal(160, entries.SelectMany(e => e.Lines).Where(l => l.AccountCode == AccountCodes.Cogs)
                .Sum(l => l.SignedMinor));
            var feed = await db.Set<ActivityEvent>().OrderBy(e => e.OccurredUtc).ThenBy(e => e.Id).ToListAsync();
            AssertFeed(expectedFeed, feed);
            var visit = await db.Set<Visit>().SingleAsync(v => v.Id == visitId);
            Assert.Equal(2, visit.ServiceLines.Count);
            Assert.Equal(12m, visit.Vitals!.WeightKg);
            Assert.Equal(38m, visit.Vitals.TemperatureC);
            var session = await db.Set<CashSession>().SingleAsync(s => s.Id == sessionId);
            Assert.Equal(450, session.ExpectedClosing.Minor);
            Assert.Equal(closed ? CashSessionState.Closed : CashSessionState.Open, session.State);
            if (closed) Assert.Equal(0, session.Variance.Minor);
        }
        using (var db = fixture.CreateContext(fixture.TenantId)) await AssertNumbers(db, false);

        // 12. Exact counted cash, never an auto-corrected variance.
        await Send(new CloseCashSessionCommand(Money.Irt(450), null));
        // 13. Dispose the DB context and reopen the SAME FILE, reassert every number/state above.
        using (var db = fixture.CreateContext(fixture.TenantId)) await AssertNumbers(db, true);

        // 14. Boarding variant: product item, checkout handoff, invoice, payment, stock/ledger/feed/reopen.
        var stayId = await Send(new CreateBoardingStayCommand(petId, "R-01", tomorrow, tomorrow.AddDays(1), 200, null));
        await Send(new AddStayItemCommand(stayId, productId, 1));
        await Send(new CheckoutStayCommand(stayId));
        var boardingReady = Assert.Single(await Query(new ReadyForBillingQuery()));
        var boardingInvoiceId = await Send(new CreateInvoiceFromStayCommand(boardingReady.Id));
        await Send(new PostInvoiceCommand(boardingInvoiceId));
        await Send(new RegisterPaymentCommand(boardingInvoiceId, PaymentMethod.Card, Money.Irt(300), "CARD-1"));
        async Task AssertBoarding(AnimoraLocalDbContext db)
        {
            Assert.Equal(InvoiceState.Paid, (await db.Set<Invoice>().SingleAsync(i => i.Id == boardingInvoiceId)).State);
            Assert.Equal(300, (await db.Set<Payment>().SingleAsync(p => p.InvoiceId == boardingInvoiceId)).Amount.Minor);
            Assert.Equal(15_000, (await db.Set<StockOnHandSnapshot>().SingleAsync(s => s.ProductId == productId)).OnHandScaled);
            Assert.Equal(15m, (await db.Set<StockMovement>().Where(m => m.ProductId == productId).ToListAsync()).Sum(m => m.Quantity.Value));
            var entries = await db.Set<JournalEntry>().ToListAsync();
            Assert.Equal(9, entries.Count);
            Assert.All(entries, e => Assert.Equal(0, e.Lines.Sum(l => l.SignedMinor)));
            Assert.Equal(700, entries.SelectMany(e => e.Lines).Where(l => l.AccountCode == AccountCodes.ProductRevenue ||
                l.AccountCode == AccountCodes.BoardingRevenue).Sum(l => -l.SignedMinor));
            var feed = await db.Set<ActivityEvent>().OrderBy(e => e.OccurredUtc).ThenBy(e => e.Id).ToListAsync();
            // The real checkout event follows invoice issuance: an unbilled stay cannot leave.
            AssertFeed(expectedFeed.Concat(new[] { ActivityEventKeys.StayActivated, ActivityEventKeys.InvoicePosted,
                ActivityEventKeys.StayCheckedOut, ActivityEventKeys.StockMovementRecorded, ActivityEventKeys.PaymentRegistered }).ToArray(),
                feed);
            var stay = await db.Set<BoardingStay>().SingleAsync(s => s.Id == stayId);
            Assert.Equal(StayState.CheckedOut, stay.State);
            Assert.Equal(boardingInvoiceId, stay.InvoiceId);
            Assert.NotNull(stay.ActualCheckOutUtc);
            Assert.All(stay.RoomHistory, room => Assert.NotNull(room.ToUtc));
            Assert.Empty(await Query(new ReadyForBillingQuery()));
            Assert.Equal(operations, (await db.Set<PendingCommand>().OrderBy(p => p.LocalSequence).ToListAsync()).Select(p => p.OperationId));
        }
        using (var db = fixture.CreateContext(fixture.TenantId)) await AssertBoarding(db);
        using (var reopened = fixture.CreateContext(fixture.TenantId)) await AssertBoarding(reopened);
    }

    /// <summary>
    /// Rows raised in the same clinic instant (e.g. an invoice post, its stock movements and the checkout it releases) share
    /// <see cref="ActivityEvent.OccurredUtc"/>; their id tie-break comes from a process-wide generator that parallel tests also
    /// advance, so the order INSIDE one instant is unspecified. The feed must match <paramref name="expected"/> instant by instant:
    /// exact order across instants, same multiset of titles within one.
    /// </summary>
    private static void AssertFeed(IReadOnlyList<string> expected, IReadOnlyList<ActivityEvent> feed)
    {
        Assert.Equal(expected.Count, feed.Count);
        var at = 0;
        foreach (var instant in feed.GroupBy(e => e.OccurredUtc))
        {
            var titles = instant.Select(e => e.Title).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected.Skip(at).Take(titles.Length).Order(StringComparer.Ordinal), titles);
            at += titles.Length;
        }
    }
}
