using System.Diagnostics;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Animora.Persistence.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>Opt-in benchmark, not a timing assertion in CI. Setup and warm-up are outside measured work.</summary>
public sealed class OfflineBudgetMeasurements
{
    [Fact]
    public async Task CaptureWarmLocalOperations()
    {
        var output = Environment.GetEnvironmentVariable("ANIMORA_BENCH_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var fixture = new LocalStoreTestFixture(configureOptions: o => o.SeedSampleData = false,
            configureServices: s => s.AddAnimoraApplication(b => b.AddSalesUseCases()));
        await fixture.InitializeAsync();
        await using (var db = fixture.CreateContext(fixture.TenantId))
        {
            var owner = Owner.Register(fixture.TenantId, "Benchmark owner", "09123456789", fixture.Clock);
            db.Add(owner);
            for (var i = 0; i < 200; i++)
                db.Add(Pet.Register(fixture.TenantId, owner.Id, $"patient-{i:000}", PetSpecies.Cat, fixture.Clock));
            await db.SaveChangesAsync();
        }
        using var scope = fixture.CreateScope();
        var services = scope.ServiceProvider;
        var patients = services.GetRequiredService<Animora.Application.Ports.IPatientDirectoryQueries>();
        var dbContext = services.GetRequiredService<AnimoraLocalDbContext>();
        var search = new GlobalSearchQueries(dbContext);
        var dashboard = new DashboardQueries(dbContext);
        var values = new Dictionary<string, double>();
        async Task Measure(string name, Func<Task> run)
        {
            await run();
            var samples = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                var watch = Stopwatch.StartNew();
                await run();
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            values[name] = samples.Order().ElementAt(2);
        }
        await Measure("patients_ms", async () =>
        {
            var result = await patients.GetPetsAsync(null, null, null, 1, 200, default);
            Assert.Equal(200, result.Items.Count);
        });
        await Measure("search_ms", async () =>
            Assert.NotEmpty(await search.SearchAsync(fixture.TenantId, "patient", 8, default)));
        await Measure("dashboard_ms", async () =>
            _ = await dashboard.GetSliceAsync(fixture.TenantId, 20260301, "benchmark", Animora.Contracts.Navigation.UserRole.Manager, default));
        // Two independently persisted ten-line drafts: first warms JIT/SQL, second is measured.
        for (var pass = 0; pass < 2; pass++)
        {
            var operation = Animora.Domain.Primitives.IdGenerator.NewId();
            var invoice = Invoice.Create(fixture.TenantId, "bench-" + operation, InvoiceKind.DirectSale,
                fixture.Clock.UtcNow, "benchmark", guestName: "Guest").Value!;
            for (var i = 0; i < 10; i++)
                Assert.True(invoice.AddLine(new InvoiceLine(Animora.Domain.Primitives.IdGenerator.NewId(),
                    InvoiceLineKind.Custom, null, null, null, $"line-{i}", new Quantity(1, "PCS"),
                    Money.Irt(1000), 0, 0, InvoiceLineSource.Manual, null)).IsSuccess);
            dbContext.Add(invoice);
            await dbContext.SaveChangesAsync();
            var context = new CommandContext(fixture.TenantId, "benchmark", "device", operation, operation,
                null, null, null, fixture.Clock.UtcNow);
            var handler = services.GetRequiredService<PostInvoiceCommandHandler>();
            var watch = Stopwatch.StartNew();
            var posted = await services.GetRequiredService<IUnitOfWork>().ExecuteAsync(
                ct => handler.HandleAsync(new PostInvoiceCommand(invoice.Id), context, ct));
            var elapsed = watch.Elapsed.TotalMilliseconds;
            Assert.True(posted.IsSuccess, posted.Detail);
            if (pass == 1) values["post_invoice_ms"] = elapsed;
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(values));
    }
}
