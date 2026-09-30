using Animora.Application.Abstractions;
using Animora.Application.Activity;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.UseCases.Identity;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Visits;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Inventory;
using Animora.Domain.Boarding;
using Animora.Domain.Cashier;
using Animora.Domain.Clinical;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests.Scenarios;

/// <summary>A continuous real dispatcher/SQLite flow. Every command uses its own scope, as the client does.</summary>
public sealed class OfflineClinicDayScenarioTests
{
    [Fact]
    public async Task FourteenSteps_SurviveReopen_WithBoardingVariant()
    {
        using var fixture = new LocalStoreTestFixture(configureOptions: o =>
        {
            o.SeedSampleData = false;
            o.AllowInsecureKeyFallback = true;
        }, configureServices: services => services.AddAnimoraApplication(b => b.AddIdentityUseCases()
            .AddPatientsUseCases().AddReceptionUseCases().AddSchedulingUseCases().AddVisitsUseCases()
            .AddInventoryUseCases().AddSalesUseCases().AddCashierUseCases().AddBoardingUseCases()));
        await fixture.InitializeAsync();
        var device = fixture.Services.GetRequiredService<ILocalInstallationIdentity>().DeviceId;
        var user = "";
        var journalOperations = new List<string>();

        async Task<T> Send<T>(ICommand<T> command)
        {
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
            var operation = IdGenerator.NewId(fixture.Clock.Now);
            using var scope = fixture.CreateScope();
            var context = new CommandContext(fixture.TenantId, user, device, operation, operation,
                null, null, null, fixture.Clock.Now);
            var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context);
            Assert.True(result.IsSuccess, $"{command.GetType().Name}: {result.Error.Code} {result.Detail}");
            if (command is IJournaledCommand) journalOperations.Add(operation);
            return result.Value!;
        }

        // 1: first run creates the owner/admin and durable offline capability, not sample credentials.
        user = (await Send(new CompleteFirstRunSetupCommand("Offline clinic", "Admin", "admin", "Secret123"))).UserId;
        string doctor, category, room;
        await using (var db = fixture.CreateContext(fixture.TenantId))
        {
            Assert.Equal(1, await db.Set<User>().CountAsync());
            Assert.Equal(StaffRole.Manager, (await db.Set<StaffMembership>().SingleAsync()).Role);
            var clinician = User.Create(fixture.TenantId, "doctor", "Doctor", fixture.Clock);
            var membership = StaffMembership.Create(fixture.TenantId, clinician.Id, StaffRole.Doctor, fixture.Clock);
            membership.SetWorkingHours(480, 1200, 127);
            db.Add(clinician); db.Add(membership);
            await db.SaveChangesAsync();
            doctor = clinician.Id;
            category = await db.Set<ProductCategory>().Select(c => c.Id).FirstAsync();
            room = await db.Set<Room>().Select(r => r.Id).FirstAsync();
        }
        var product = await Send(new CreateProductCommand("OFF-1", "Offline product", category, "PCS",
            ProductKind.Medicine, 100, 1000, [new(null, "OFF-1-V", "Pack", "One", 100, 1000, 0)]));
        string variant;
        await using (var db = fixture.CreateContext(fixture.TenantId))
            variant = (await db.Set<Product>().SingleAsync(p => p.Id == product)).Variants.Single().VariantId;
        // Two explicitly journaled opening-stock deliveries make the expected feed exactly nine events.
        await Send(new AdjustStockCommand(product, variant, 10000, AdjustmentReasons.Count));
        await Send(new AdjustStockCommand(product, variant, 10000, AdjustmentReasons.Correction));

        // 2: owner and pet registration.
        var owner = await Send(new CreateOwnerCommand("Owner", "09123456789", OwnerGender.Unspecified,
            null, null, null, null, null, null, null, null, null, null, null, false));
        var pet = await Send(new CreatePetCommand(owner, "Pet", PetSpecies.Cat, null, null,
            PetGender.Unknown, null, null, null, false));

        // 3: appointment tomorrow; clock is advanced before check-in.
        var tomorrow = DateOnly.FromDateTime(fixture.Clock.Now.UtcDateTime).AddDays(1);
        var appointment = await Send(new BookAppointmentCommand(tomorrow, 600, 30,
            AppointmentTypeCatalog.All[0].Code, doctor, pet, owner, null));
        fixture.Clock.Now = fixture.Clock.Now.AddDays(1);
        await Send(new CheckInAppointmentCommand(appointment));

        // 4: derived queue -> admit -> encounter.
        var ticket = "apt:" + appointment;
        using (var scope = fixture.CreateScope())
        {
            var queue = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                .QueryAsync(new ReceptionQueueQuery(tomorrow), new QueryContext(fixture.TenantId, user, "queue"));
            Assert.True(queue.IsSuccess, queue.Detail);
            Assert.Contains(queue.Value!, q => q.TicketId == ticket);
        }
        await Send(new AdmitTicketCommand(ticket));
        var visit = (await Send(new StartVisitFromTicketCommand(ticket))).VisitId;

        // 5: vitals, diagnosis and two Rx lines; complete to a billing handoff.
        await Send(new SaveVisitVitalsCommand(visit, 4.2m, 38.5m, 120, 30));
        await Send(new SaveVisitNoteCommand(visit, VisitNoteField.Diagnosis, "DX-1"));
        await Send(new AddPrescriptionLineCommand(visit, "RX-1", "Rx one", VisitLineSource.Custom, 500, 1));
        await Send(new AddPrescriptionLineCommand(visit, "RX-2", "Rx two", VisitLineSource.Custom, 500, 1));
        await Send(new CompleteVisitCommand(visit));
        string handoff;
        await using (var db = fixture.CreateContext(fixture.TenantId))
        {
            Assert.Equal(2, (await db.Set<Visit>().SingleAsync(v => v.Id == visit)).ServiceLines.Count);
            handoff = (await db.Set<VisitBillingHandoff>().SingleAsync()).Id;
        }

        // 6: invoice from handoff, with exactly one product line.
        var invoice = await Send(new CreateInvoiceFromVisitCommand(handoff));
        await Send(new AddInvoiceLineCommand(invoice, variant, 2));
        await Send(new PostInvoiceCommand(invoice));
        long total;
        await using (var db = fixture.CreateContext(fixture.TenantId))
            total = (await db.Set<Invoice>().SingleAsync(i => i.Id == invoice)).GrandTotal.Minor;
        Assert.Equal(3000, total);

        // 7: partial payment then the rest in one cash session.
        var session = await Send(new OpenCashSessionCommand(Money.Irt(100)));
        await Send(new RegisterPaymentCommand(invoice, PaymentMethod.Cash, Money.Irt(1000)));
        await Send(new RegisterPaymentCommand(invoice, PaymentMethod.Cash, Money.Irt(total - 1000)));
        string[] feed = [ActivityEventKeys.StockMovementRecorded, ActivityEventKeys.StockMovementRecorded,
            ActivityEventKeys.PetRegistered, ActivityEventKeys.AppointmentBooked, ActivityEventKeys.VisitCompleted,
            ActivityEventKeys.InvoicePosted, ActivityEventKeys.StockMovementRecorded,
            ActivityEventKeys.PaymentRegistered, ActivityEventKeys.PaymentRegistered];

        // 8–11: durable command count, stock proof, balanced ledger, exact ordered feed.
        async Task VerifyAsync(AnimoraLocalDbContext db, bool closed, long expectedStock, long expectedRevenue,
            string[] expectedFeed)
        {
            var paid = await db.Set<Invoice>().SingleAsync(i => i.Id == invoice);
            Assert.Equal(InvoiceState.Paid, paid.State);
            Assert.Equal(total, paid.GrandTotal.Minor);
            Assert.Equal(total, paid.PaidTotal.Minor);
            Assert.Equal(0, paid.DueTotal.Minor);
            Assert.Equal(2, await db.Set<Payment>().CountAsync(p => p.InvoiceId == invoice));
            var pending = await db.Set<PendingCommand>().ToListAsync();
            Assert.Equal(journalOperations.Order(), pending.Select(p => p.OperationId).Order());
            Assert.All(pending, p => Assert.Contains(p.Status,
                new[] { SettlementState.LocallyPosted, SettlementState.PendingSettlement }));
            Assert.Equal(expectedStock, (await db.Set<StockOnHandSnapshot>()
                .SingleAsync(s => s.ProductId == product && s.VariantId == variant)).OnHandScaled);
            var movements = await db.Set<StockMovement>().Where(m => m.ProductId == product).ToListAsync();
            Assert.Equal(expectedStock, (long)(movements.Sum(m => m.Quantity.Value) * 1000m));
            var ledger = await db.Set<JournalEntry>().ToListAsync();
            Assert.NotEmpty(ledger);
            Assert.All(ledger, e => Assert.Equal(0, e.Lines.Sum(l => l.SignedMinor)));
            Assert.Equal(expectedRevenue, ledger.SelectMany(e => e.Lines)
                .Where(l => l.AccountCode.StartsWith("4", StringComparison.Ordinal)).Sum(l => -l.SignedMinor));
            Assert.Equal(expectedFeed, await db.Set<ActivityEvent>().OrderBy(e => e.OccurredUtc)
                .ThenBy(e => e.Id).Select(e => e.Title).ToArrayAsync());
            var drawer = await db.Set<CashSession>().SingleAsync(s => s.Id == session);
            Assert.Equal(3100, drawer.ExpectedClosing.Minor);
            Assert.Equal(closed ? CashSessionState.Closed : CashSessionState.Open, drawer.State);
            Assert.Equal(0, drawer.Variance.Minor);
        }
        await using (var db = fixture.CreateContext(fixture.TenantId))
            await VerifyAsync(db, false, 18000, total, feed);
        // 12: exact cash count; offline locally-posted payments do not block close.
        await Send(new CloseCashSessionCommand(Money.Irt(3100), null));
        // 13: all contexts/scopes above disposed; fresh context over the same file, every number reasserted.
        await using (var reopened = fixture.CreateContext(fixture.TenantId))
            await VerifyAsync(reopened, true, 18000, total, feed);

        // 14: boarding + product item + checkout + invoice; stock, ledger and feed consequences repeat.
        var stay = await Send(new CreateBoardingStayCommand(pet, room, tomorrow, tomorrow.AddDays(1), 1000, null));
        await Send(new AddStayItemCommand(stay, variant, 1));
        fixture.Clock.Now = fixture.Clock.Now.AddDays(1);
        await Send(new CheckoutStayCommand(stay));
        string stayHandoff;
        await using (var db = fixture.CreateContext(fixture.TenantId))
            stayHandoff = (await db.Set<BoardingBillingHandoff>().SingleAsync()).Id;
        var boardingInvoice = await Send(new CreateInvoiceFromStayCommand(stayHandoff));
        await Send(new PostInvoiceCommand(boardingInvoice));
        long boardingTotal;
        await using (var db = fixture.CreateContext(fixture.TenantId))
        {
            var row = await db.Set<Invoice>().SingleAsync(i => i.Id == boardingInvoice);
            boardingTotal = row.GrandTotal.Minor;
            Assert.Equal(2000, boardingTotal);
            Assert.Equal(StayState.ReadyForInvoice, (await db.Set<BoardingStay>().SingleAsync()).State);
        }
        string[] extended = [.. feed, ActivityEventKeys.StayActivated, ActivityEventKeys.StayCheckedOut,
            ActivityEventKeys.InvoicePosted, ActivityEventKeys.StockMovementRecorded];
        await using (var reopened = fixture.CreateContext(fixture.TenantId))
            await VerifyAsync(reopened, true, 17000, total + boardingTotal, extended);
    }
}
