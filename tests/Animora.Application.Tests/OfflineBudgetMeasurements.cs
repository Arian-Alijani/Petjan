using System.Diagnostics;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Dashboard;
using Animora.Application.UseCases.Identity;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Search;
using Animora.Contracts.Navigation;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Domain.Primitives;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Seed;
using Animora.Persistence.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>Opt-in measurement harness, not performance assertions in CI. bench.sh applies the 2× ceiling.</summary>
public sealed class OfflineBudgetMeasurements
{
    [Fact]
    public async Task CaptureWarmLocalOperations()
    {
        var output = Environment.GetEnvironmentVariable("ANIMORA_BENCH_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        using var fixture = new LocalStoreTestFixture(configureOptions: o => { o.AllowInsecureKeyFallback = true; o.SeedSampleData = false; },
            configureServices: s => s.AddAnimoraApplication(b => b.AddIdentityUseCases().AddSalesUseCases()
                .AddDashboardUseCases().AddSearchUseCases()));
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<LocalSeeder>().SeedAsync()).IsSuccess);
        string userId;
        using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildAnonymousCommandContext().Value!;
            var setup = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                .SendAsync(new CompleteFirstRunSetupCommand("Clinic", "Admin", "admin", "Secret123"), context);
            Assert.True(setup.IsSuccess, setup.Detail);
            userId = setup.Value!.UserId;
        }
        string ownerId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var owner = Owner.Register(fixture.TenantId, "Benchmark Owner", "09120000001", fixture.Clock);
            ownerId = owner.Id;
            db.Add(owner);
            for (var i = 0; i < 200; i++) db.Add(Pet.Register(fixture.TenantId, ownerId, $"Patient-{i:000}", PetSpecies.Dog, fixture.Clock));
            await db.SaveChangesAsync();
        }
        async Task Send(ICommand<Animora.Application.Abstractions.Unit> command)
        {
            using var scope = fixture.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildAnonymousCommandContext().Value!
                with { UserId = userId };
            var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context);
            Assert.True(result.IsSuccess, $"{result.Error.Code}: {result.Detail}");
        }
        // Five independent ten-line drafts; no measured setup, no replay-as-post shortcut.
        var invoices = new List<string>();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            for (var i = 0; i < 5; i++)
            {
                var invoice = Invoice.Create(fixture.TenantId, "bench-" + i, InvoiceKind.DirectSale,
                    fixture.Clock.Now, userId, ownerId).Value!;
                for (var j = 0; j < 10; j++)
                    Assert.True(invoice.AddLine(new InvoiceLine(IdGenerator.NewId(), InvoiceLineKind.Service,
                        null, null, null, "Service", new Quantity(1, "PCS"), Money.Irt(100), 0, 0,
                        InvoiceLineSource.Manual, null)).IsSuccess);
                db.Add(invoice);
                invoices.Add(invoice.Id);
            }
            await db.SaveChangesAsync();
        }
        var metrics = new Dictionary<string, double>();
        async Task<double> Measure(Func<Task> action)
        {
            var samples = new List<double>();
            for (var i = 0; i < 5; i++) { var timer = Stopwatch.StartNew(); await action(); samples.Add(timer.Elapsed.TotalMilliseconds); }
            return samples.Order().ElementAt(2);
        }
        using (var scope = fixture.CreateScope())
        {
            var patients = scope.ServiceProvider.GetRequiredService<IPatientDirectoryQueries>();
            async Task Patients() { var page = await patients.GetPetsAsync(null, null, null, 1, 200, default); Assert.Equal(200, page.Items.Count); }
            await Patients();
            metrics["patients_ms"] = await Measure(Patients);
            var search = scope.ServiceProvider.GetRequiredService<IGlobalSearchQueries>();
            async Task Search() { var hits = await search.SearchAsync(fixture.TenantId, "Patient", 8, default); Assert.NotEmpty(hits); }
            await Search();
            metrics["search_ms"] = await Measure(Search);
            var dashboard = scope.ServiceProvider.GetRequiredService<DashboardComposer>();
            async Task Dashboard() { var result = await dashboard.HandleAsync(new DashboardSnapshotQuery(UserRole.Manager),
                new QueryContext(fixture.TenantId, userId, "bench"), default); Assert.True(result.IsSuccess); }
            await Dashboard();
            metrics["dashboard_ms"] = await Measure(Dashboard);
        }
        await Send(new PostInvoiceCommand(invoices[0])); // Warm the complete real posting pipeline.
        var postSamples = new List<double>();
        foreach (var id in invoices.Skip(1)) { var timer = Stopwatch.StartNew(); await Send(new PostInvoiceCommand(id)); postSamples.Add(timer.Elapsed.TotalMilliseconds); }
        metrics["post_ms"] = postSamples.Order().ElementAt(postSamples.Count / 2);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(metrics));
    }
}
