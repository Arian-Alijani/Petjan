using System.Text;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Boarding;
using Animora.Domain.Cashier;
using Animora.Domain.Clinical;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Purchasing;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class RandomData
{
    private static readonly string[] FirstNames = ["علی", "مریم", "رضا", "زهرا", "سارا", "امیر", "نازنین", "حسین"];
    private static readonly string[] LastNames = ["رضایی", "حسینی", "کریمی", "احمدی", "موسوی", "نوری", "جعفری", "قاسمی"];
    private static readonly string[] PetNames = ["میلو", "لونا", "سیمبا", "نالا", "کوکو", "رکس", "پشمالو", "برفی"];
    private static readonly string[] ProductNames = ["غذای خشک", "شامپو", "تشویقی", "قلاده", "مکمل", "ظرف آب", "اسباب‌بازی", "بستر حیوان"];
    private static readonly string[] Cities = ["تهران", "کرج", "شیراز", "اصفهان", "رشت"];
    private static readonly string[] Diagnoses = ["معاینه دوره‌ای", "بررسی پوست", "پیگیری درمان", "بررسی اشتها", "کنترل وزن"];
    private static readonly PetSpecies[] Species = [PetSpecies.Dog, PetSpecies.Cat, PetSpecies.Bird, PetSpecies.Rodent];

    public static async Task PopulateAsync(ServiceProvider provider, int count)
    {
        var random = new Random();
        var now = DateTimeOffset.UtcNow;
        string[] doctors;
        string actor;
        Room[] rooms;
        Product[] products;
        Supplier[] suppliers;
        string documentHash;
        var document = Encoding.UTF8.GetBytes("Synthetic manual-test document. Not a real clinical report.\n");
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AnimoraLocalDbContext>();
            var tenant = sp.GetRequiredService<ILocalTenantContext>().TenantId;
            actor = await db.Set<StaffMembership>().Where(m => m.Role == StaffRole.Manager)
                .Select(m => m.UserId).FirstAsync();
            var periodCodes = sp.GetRequiredService<ISeedPeriodCodeProvider>();
            // Real journal posting handlers require an open period for every historical accounting date.
            var knownPeriods = (await db.Set<AccountingPeriod>().Select(p => p.PeriodCode).ToListAsync())
                .ToHashSet(StringComparer.Ordinal);
            for (var day = -180; day <= 40; day += 7)
            {
                var period = periodCodes.For(now.AddDays(day));
                if (knownPeriods.Add(period.PeriodCode))
                    db.Add(Value(AccountingPeriod.Create(tenant, period.PeriodCode, period.StartUtc, period.EndUtc)));
            }
            doctors = new string[4];
            for (var d = 0; d < doctors.Length; d++)
            {
                var doctor = User.Create(tenant, $"random-doctor-{d}", $"دامپزشک آزمایشی {d + 1}", new AtClock(now));
                doctors[d] = doctor.Id;
                db.Add(doctor);
                var membership = StaffMembership.Create(tenant, doctor.Id, StaffRole.Doctor, new AtClock(now));
                membership.SetWorkingHours(480, 1200, 127);
                db.Add(membership);
            }
            var category = await db.Set<ProductCategory>().Where(c => c.Code == "ACC").Select(c => c.Id).FirstAsync();
            // Bounded reusable catalogue; the requested count scales business scenarios, not staff/rooms.
            for (var p = 0; p < Math.Min(count, 500); p++)
            {
                var cost = Money.Irt(random.Next(20, 400) * 1000L);
                var sale = Money.Irt(cost.Minor + random.Next(10, 150) * 1000L);
                var variant = new ProductVariant(IdGenerator.NewId(now), $"RND-{p:00000}-1", "SIZE", "STD",
                    cost, sale, new Quantity(10, "PCS"), null, true);
                var product = Value(Product.Create(tenant, $"RND-{p:00000}", $"{Pick(ProductNames, random)} آزمایشی {p + 1}",
                    category, ProductKind.PetShop, "PCS", cost, sale, [variant], reorderLevel: new Quantity(10, "PCS"), brand: "Test"));
                db.Add(product);
                db.Add(Value(StockMovement.Create(tenant, product.Id, variant.VariantId, MovementKind.Receipt,
                    new Quantity(random.Next(0, 25) + 1, "PCS"), cost, now.AddDays(-120), actor,
                    sourceEntityType: "ManualTest", reason: "Synthetic opening stock")));
            }
            for (var s = 0; s < Math.Min(Math.Max(1, count / 50), 100); s++)
                db.Add(Value(Supplier.Create(tenant, $"RND-SUP-{s:000}", $"پخش آزمایشی {Pick(LastNames, random)} {s + 1}",
                    random.Next(0, 4) * 15, contactPerson: "مسئول تست", phone: "ثبت نشده")));
            Check(await sp.GetRequiredService<IUnitOfWork>().SaveAsync());
            products = await db.Set<Product>().AsNoTracking().Where(p => p.Sku.StartsWith("RND-"))
                .OrderBy(p => p.Sku).ToArrayAsync();
            suppliers = await db.Set<Supplier>().AsNoTracking().OrderBy(s => s.Id).Take(110).ToArrayAsync();
            rooms = await db.Set<Room>().AsNoTracking().OrderBy(r => r.Code).ToArrayAsync();
            // One usable live drawer; historical synthetic receipts use separate closed sessions.
            db.Add(Value(CashSession.Open(tenant, "RND-CS-LIVE", actor, Money.Irt(1_000_000), now)));
            Check(await sp.GetRequiredService<IUnitOfWork>().SaveAsync());
            await using var content = new MemoryStream(document);
            documentHash = Value(await sp.GetRequiredService<ILocalFileStore>().PutAsync(content, default));
        }

        // Fresh scopes + transactions keep the change tracker bounded even for 5000+ scenarios.
        for (var offset = 0; offset < count; offset += 25)
        {
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AnimoraLocalDbContext>();
            var tenant = sp.GetRequiredService<ILocalTenantContext>().TenantId;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var end = Math.Min(count, offset + 25);
            Value(await uow.ExecuteAsync(async ct =>
            {
                for (var i = offset; i < end; i++)
                {
                    // Force examples today, this week and this month as well as a four-month history.
                    var at = i < 8 ? now.AddMinutes(-30) : i < 16 ? now.AddDays(-random.Next(1, 7))
                        : i < 32 ? now.AddDays(-random.Next(7, 28)) : now.AddDays(-random.Next(0, 120));
                    var clock = new AtClock(at);
                    var name = $"{Pick(FirstNames, random)} {Pick(LastNames, random)} آزمایشی {i + 1}";
                    // No routable synthetic phone numbers: the app's explicit unknown label normalizes to empty.
                    var owner = Owner.Register(tenant, name, "ثبت نشده", clock);
                    owner.UpdateContactDetails(name, "ثبت نشده", email: $"owner{i + 1}@example.invalid",
                        city: Pick(Cities, random), address: "نشانی ساختگی برای تست", notes: "داده مصنوعی؛ غیرعملیاتی");
                    db.Add(owner);
                    var pet = Pet.Register(tenant, owner.Id, $"{Pick(PetNames, random)} {i + 1}", Pick(Species, random), clock,
                        i % 2 == 0 ? PetGender.Male : PetGender.Female, at.AddMonths(-random.Next(6, 100)), "دورگه");
                    db.Add(pet);
                    for (var w = 0; w < 3; w++)
                        db.Add(Value(WeightEntry.Record(tenant, pet.Id, random.Next(10, 250) / 10m,
                            at.AddDays(-60 + w * 30), actor, clock)));
                    db.Add(Value(DocumentReference.Attach(tenant, "گزارش ساختگی تست", DocumentKind.Other,
                        documentHash, document.Length, documentHash, actor, clock, ownerId: owner.Id, petId: pet.Id)));

                    var doctor = doctors[i % doctors.Length];
                    // New doctors, non-overlapping 30-minute slots. Today + a recent past/future calendar.
                    var day = i < 32 ? 0 : (i - 32) / 32 - Math.Max(1, (count - 32 + 63) / 64);
                    if (i >= 32 && day >= 0) day++; // Never repeat today's baseline slots.
                    var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(day)
                        .AddHours(6).AddMinutes((i % 32 / 4) * 30);
                    var appointment = Value(Appointment.Book(tenant, pet.Id, owner.Id, "CHECKUP", start, 30,
                        AppointmentSource.Phone, actor, clock, doctor));
                    if (i % 5 == 0) Check(appointment.Cancel("لغو آزمایشی", clock));
                    else if (start < now && i % 5 == 1) Check(appointment.MarkNoShow(clock));
                    else if (i % 2 == 0) Check(appointment.Confirm());
                    db.Add(appointment);

                    var visit = Visit.Open(tenant, pet.Id, owner.Id, doctor, (VisitReason)(i % 7), clock,
                        chiefComplaint: Pick(Diagnoses, random));
                    Check(visit.Start());
                    Check(visit.SetVitals(new PetVitals(random.Next(10, 250) / 10m, 38m + random.Next(0, 10) / 10m,
                        random.Next(80, 130), random.Next(18, 30), null, null)));
                    Check(visit.SetAssessment(Pick(Diagnoses, random), "مراقبت و پیگیری آزمایشی", "پرونده ساختگی"));
                    Check(visit.AddPrescription(new PrescriptionLine(IdGenerator.NewId(at), null,
                        "داروی نمونه (صرفاً تست)", "دوز آزمایشی", "BID", random.Next(3, 10), "نسخه غیرواقعی؛ استفاده نشود")));
                    var fee = Money.Irt(random.Next(100, 450) * 1000L);
                    Check(visit.AddServiceLine(new VisitServiceLine(IdGenerator.NewId(at), null,
                        "معاینه آزمایشی", new Quantity(1, "PCS"), fee, VisitLineSource.Custom)));
                    if (i % 10 != 0) { Check(visit.Complete(clock)); Check(visit.MarkReadyForInvoice()); }
                    db.Add(visit);
                    var queue = ReceptionQueueEntry.Create(tenant, pet.Id, owner.Id, QueueSource.WalkIn,
                        1000 + i, clock, note: "پذیرش آزمایشی");
                    if (i % 10 != 0)
                    {
                        Check(queue.Call(doctor, clock)); Check(queue.StartVisit(visit.Id)); Check(queue.Close(clock));
                    }
                    db.Add(queue);
                    db.Add(new MedicalHistoryEntry(tenant, pet.Id, MedicalHistoryKind.Visit, "ویزیت آزمایشی",
                        at, doctor, clock, visit.Diagnosis, visit.Id, "Visit"));
                    const string vaccine = "RABIES";
                    var due = now.AddDays(new[] { -14, -3, 0, 3, 14, 30, 90 }[i % 7]);
                    var administered = due.AddDays(-VaccineSchedule.IntervalFor(vaccine, 1));
                    db.Add(Value(VaccinationRecord.Administer(tenant, pet.Id, vaccine, "هاری (نمونه تست)", 1,
                        administered, doctor, new AtClock(administered), totalDoses: 1)));

                    var product = products[random.Next(products.Length)];
                    var variant = product.Variants[0];
                    var quantity = random.Next(1, 4);
                    var supplier = suppliers[random.Next(suppliers.Length)];
                    AddPurchase(db, tenant, actor, supplier, product, variant, i, clock);
                    var invoice = Value(Invoice.Create(tenant, "draft-" + IdGenerator.NewId(at),
                        visit.State == VisitState.ReadyForInvoice ? InvoiceKind.Visit : InvoiceKind.DirectSale,
                        at, actor, owner.Id, null, pet.Id, operationId: IdGenerator.NewId(at), notes: "داده مصنوعی؛ صرفاً تست"));
                    if (visit.State == VisitState.ReadyForInvoice) Check(invoice.AttachVisit(visit.Id));
                    Check(invoice.AddLine(new InvoiceLine(IdGenerator.NewId(at), InvoiceLineKind.Product,
                        variant.VariantId, product.Id, variant.VariantId, product.Name, new Quantity(quantity, "PCS"),
                        variant.SalePrice, i % 4 == 0 ? 5m : 0m, 9m, InvoiceLineSource.Catalog, null)));
                    Check(invoice.AddLine(new InvoiceLine(IdGenerator.NewId(at), InvoiceLineKind.Service,
                        null, null, null, "معاینه آزمایشی", new Quantity(1, "PCS"), fee, 0m, 9m,
                        visit.State == VisitState.ReadyForInvoice ? InvoiceLineSource.Visit : InvoiceLineSource.Manual, visit.Id)));
                    if (i % 6 != 0)
                    {
                        Check(invoice.AssignInvoiceNumber(await sp.GetRequiredService<IInvoiceNumberAllocator>().AllocateAsync(tenant, at, ct)));
                        Check(invoice.Post(clock));
                        db.Add(Value(StockMovement.Create(tenant, product.Id, variant.VariantId, MovementKind.Receipt,
                            new Quantity(quantity + random.Next(1, 20), "PCS"), variant.CostPrice, at, actor,
                            sourceEntityType: "ManualTest", reason: "Synthetic replenishment")));
                        db.Add(Value(StockMovement.Create(tenant, product.Id, variant.VariantId, MovementKind.Sale,
                            new Quantity(-quantity, "PCS"), variant.CostPrice, at, actor,
                            sourceEntityType: "Invoice", sourceEntityId: invoice.Id, operationId: invoice.OperationId, reason: "Synthetic sale")));
                        var sequence = Value(await sp.GetRequiredService<ILocalSequenceAllocator>().NextAsync(ct));
                        var payload = JsonSerializer.Serialize(new { invoiceId = invoice.Id });
                        db.Add(PendingCommand.Create(CommandEnvelope.Create(invoice.OperationId!, tenant, actor, "D-MANUAL-TEST",
                            CommandTypes.SalesInvoicePost, "Invoice", invoice.Id, payload,
                            CommandFingerprint.Compute(CommandTypes.SalesInvoicePost, payload), at, sequence), at));
                        if (i % 6 is 2 or 3 or 4 or 5)
                        {
                            var method = i % 3 == 0 ? PaymentMethod.Cash : i % 3 == 1 ? PaymentMethod.Card : PaymentMethod.Transfer;
                            var amount = new Money(i % 6 == 2 ? invoice.DueTotal.Minor / 2 : invoice.DueTotal.Minor, invoice.Currency);
                            var session = method == PaymentMethod.Cash ? Value(CashSession.Open(tenant, $"RND-CS-{i:000000}",
                                actor, Money.Irt(100_000), at.AddMinutes(-5))) : null;
                            var payment = Value(Payment.Create(tenant, invoice.Id, method, amount, at, actor,
                                cashSessionId: session?.Id, cardReference: method == PaymentMethod.Card ? $"TEST-POS-{i}" : null,
                                operationId: IdGenerator.NewId(at), note: "پرداخت ساختگی"));
                            Check(invoice.ApplyPayment(payment)); db.Add(payment);
                            if (session is not null)
                            {
                                Check(session.AddMovement(Value(CashMovement.Create(CashMovementKind.SalePayment,
                                    amount, "Synthetic receipt", at, actor, invoice.Id, payment.Id, payment.OperationId))));
                                Check(session.BeginClose(session.ExpectedClosing, null, clock, false));
                                Check(session.Close(clock)); db.Add(session);
                            }
                        }
                    }
                    db.Add(invoice);
                    if (visit.State == VisitState.ReadyForInvoice)
                    {
                        var handoff = VisitBillingHandoff.Create(tenant, visit.Id, owner.Id, pet.Id, doctor, at,
                            JsonSerializer.Serialize(visit.ServiceLines));
                        if (invoice.State != InvoiceState.Draft)
                        {
                            Check(visit.AttachInvoice(invoice.Id)); handoff.MarkInvoiced(invoice.Id);
                        }
                        db.Add(handoff);
                    }
                    var expense = Value(Expense.Create(tenant,
                        await sp.GetRequiredService<IExpenseNumberAllocator>().AllocateAsync(tenant, at, ct),
                        ExpenseCategories.All[i % ExpenseCategories.All.Count], "هزینه آزمایشی " + (i + 1),
                        Money.Irt(random.Next(50, 1200) * 1000L), at, actor, settledMethod: PaymentMethod.Transfer,
                        supplierId: supplier.Id, note: "داده مصنوعی"));
                    if (i % 5 != 0) Check(expense.Post(clock));
                    db.Add(expense);
                    var cheque = Value(Cheque.Register(tenant, $"RND-CH-{i:000000}",
                        i % 2 == 0 ? ChequeDirection.Inbound : ChequeDirection.Outbound,
                        Money.Irt(random.Next(200, 3000) * 1000L), now.AddDays(random.Next(-14, 60)), at,
                        ownerId: i % 2 == 0 ? owner.Id : null, supplierId: i % 2 != 0 ? supplier.Id : null,
                        bankName: BankDirectory.All[i % BankDirectory.All.Count].Name, note: "چک ساختگی؛ فاقد اعتبار"));
                    if (i % 4 != 0) Check(cheque.MoveToPortfolio(actor, clock));
                    if (i % 4 is 2 or 3) Check(cheque.Deposit(actor, clock));
                    if (i % 4 == 3) Check(cheque.MarkBounced(actor, "برگشت آزمایشی", clock));
                    db.Add(cheque);
                    // Live occupancy is bounded by room capacity; history uses non-overlapping room stays.
                    if (i < 8)
                    {
                        var checkIn = i is 4 or 5 ? now.AddDays(3) : now.AddDays(-1);
                        var stay = Value(BoardingStay.Reserve(tenant, pet.Id, owner.Id, rooms[i].Id,
                            checkIn, checkIn.AddDays(4), Money.Irt(250_000)));
                        if (i is not (4 or 5)) Check(stay.Activate(new AtClock(checkIn)));
                        if (i >= 6)
                        {
                            Check(stay.RequestCheckout(new AtClock(now)));
                            var total = StayChargeCalculator.Accrued(stay, [], now, TimeZoneInfo.Utc);
                            Check(stay.FreezeTotalForCheckout(total));
                            db.Add(BoardingBillingHandoff.Create(tenant, stay.Id, owner.Id, pet.Id, now,
                                JsonSerializer.Serialize(new { TotalMinor = total.Minor, Currency = total.Currency, stay.Items })));
                        }
                        db.Add(stay);
                    }
                    if (i % 4 == 0 && i < 128)
                        await AddHistoricalStayAsync(sp, db, tenant, actor, owner, pet, rooms[(i / 4) % rooms.Length],
                            i, now, ct);
                }
                return Result<bool>.Ok(true);
            }));
            Console.WriteLine($"==> Synthetic scenarios: {end:N0}/{count:N0}");
        }
    }

    private static async Task AddHistoricalStayAsync(IServiceProvider sp, AnimoraLocalDbContext db,
        string tenant, string actor, Owner owner, Pet pet, Room room, int index, DateTimeOffset now, CancellationToken ct)
    {
        // Eight rooms, one 2-day stay per room per 3-day block, all ending before live occupancy.
        var end = now.AddDays(-20 - index / 32 * 3);
        var start = end.AddDays(-2);
        var clock = new AtClock(end);
        var stay = Value(BoardingStay.Reserve(tenant, pet.Id, owner.Id, room.Id, start, end, Money.Irt(250_000)));
        Check(stay.Activate(new AtClock(start))); Check(stay.RequestCheckout(clock));
        var total = StayChargeCalculator.Accrued(stay, [], end, TimeZoneInfo.Utc);
        Check(stay.FreezeTotalForCheckout(total));
        var number = await sp.GetRequiredService<IInvoiceNumberAllocator>().AllocateAsync(tenant, end, ct);
        var invoice = Value(Invoice.Create(tenant, number, InvoiceKind.Boarding, end, actor, owner.Id,
            petId: pet.Id, operationId: IdGenerator.NewId(end), notes: "پانسیون ساختگی"));
        Check(invoice.AttachStay(stay.Id));
        Check(invoice.AddLine(new InvoiceLine(IdGenerator.NewId(end), InvoiceLineKind.Boarding, null, null, null,
            "اقامت آزمایشی", new Quantity(1, "PCS"), total, 0m, 0m, InvoiceLineSource.Stay, stay.Id)));
        Check(invoice.Post(clock)); Check(stay.AttachInvoice(invoice.Id)); Check(stay.CompleteCheckout(clock));
        var handoff = BoardingBillingHandoff.Create(tenant, stay.Id, owner.Id, pet.Id, end,
            JsonSerializer.Serialize(new { TotalMinor = total.Minor, Currency = total.Currency, stay.Items }));
        handoff.MarkInvoiced(invoice.Id);
        var payload = JsonSerializer.Serialize(new { invoiceId = invoice.Id });
        var sequence = Value(await sp.GetRequiredService<ILocalSequenceAllocator>().NextAsync(ct));
        db.Add(PendingCommand.Create(CommandEnvelope.Create(invoice.OperationId!, tenant, actor, "D-MANUAL-TEST",
            CommandTypes.SalesInvoicePost, "Invoice", invoice.Id, payload,
            CommandFingerprint.Compute(CommandTypes.SalesInvoicePost, payload), end, sequence), end));
        db.Add(stay); db.Add(invoice); db.Add(handoff);
    }

    private static void AddPurchase(AnimoraLocalDbContext db, string tenant, string actor, Supplier supplier,
        Product product, ProductVariant variant, int i, AtClock clock)
    {
        var at = clock.UtcNow;
        var order = Value(PurchaseOrder.Create(tenant, $"RND-PO-{i:000000}", supplier.Id, at, actor, expectedUtc: at.AddDays(7)));
        var line = new PurchaseOrderLine(IdGenerator.NewId(at), product.Id, variant.VariantId,
            new Quantity(20, "PCS"), Quantity.Zero("PCS"), variant.CostPrice, 0m, 9m, $"TEST-LOT-{i}", at.AddYears(2));
        Check(order.AddLine(line));
        if (i % 5 != 0) Check(order.Submit(clock));
        if (i % 5 is 2 or 3)
        {
            var received = new Quantity(i % 5 == 2 ? 10 : 20, "PCS");
            var receipt = Value(GoodsReceipt.Create(tenant, $"RND-GR-{i:000000}", supplier.Id, at, actor, purchaseOrderId: order.Id));
            Check(receipt.AddLine(new GoodsReceiptLine(IdGenerator.NewId(at), product.Id, variant.VariantId,
                received, variant.CostPrice, line.LotNumber, line.ExpiryUtc)));
            Check(receipt.Post(clock)); Check(order.ApplyReceipt(receipt)); db.Add(receipt);
            db.Add(Value(StockMovement.Create(tenant, product.Id, variant.VariantId, MovementKind.Receipt,
                received, variant.CostPrice, at, actor, lotNumber: line.LotNumber, expiryUtc: line.ExpiryUtc,
                sourceEntityType: "GoodsReceipt", sourceEntityId: receipt.Id, reason: "Synthetic purchase")));
        }
        if (i % 5 == 4) Check(order.Cancel("لغو آزمایشی"));
        db.Add(order);
    }

    private static T Pick<T>(T[] values, Random random) => values[random.Next(values.Length)];
    private static T Value<T>(Result<T> result) => result.IsSuccess ? result.Value! :
        throw new InvalidOperationException($"Synthetic-data operation failed: {result.Error.Code}");
    private static void Check(Result result)
    {
        if (result.IsFailure) throw new InvalidOperationException($"Synthetic-data operation failed: {result.Error.Code}");
    }
    private sealed class AtClock(DateTimeOffset at) : IClock { public DateTimeOffset UtcNow => at; }
}
