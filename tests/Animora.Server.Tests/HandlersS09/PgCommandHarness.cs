using System.Reflection;
using System.Text.Json;
using Animora.Application;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Persistence.PostgreSql.Transactions;
using Animora.Server.Commands;
using Animora.Server.Handlers;
using Animora.Server.Tests.Commands;
using Animora.Server.Tests.HandlersS07;
using Animora.Server.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Animora.Contracts.Wire.Commands;
namespace Animora.Server.Tests.HandlersS09;

/// <summary>Actual intake store, PostgreSQL ports, retrying transaction/savepoints, versions and resource guard.</summary>
internal sealed class PgCommandHarness : IAsyncDisposable
{
    public IntakeTestFixture Intake { get; }
    public AnimoraServerDbContext Db { get; }
    public ServiceProvider Provider { get; }
    public CommandIntakeService Service { get; }
    public PgCommandHarness(string connection, string? tenantId = null)
    {
        Intake = new IntakeTestFixture(tenantId);
        var tenant = new FixedTenantContext(Intake.TenantId);
        Db = TestSupport.CreateContext(tenant, connection);
        var services = new ServiceCollection(); services.AddSingleton(Db);
        services.AddSingleton<Animora.Domain.Time.IClock>(Intake.Clock);
        services.AddAnimoraClinicalPersistence().AddAnimoraSchedulingBoardingPersistence().AddAnimoraSalesCashierPersistence();
        services.AddAnimoraApplication(h => { ServerHandlerModuleS07.AddHandlers(h); ServerHandlerModuleS08.AddHandlers(h); ServerHandlerModuleS09.AddHandlers(h); },
            ApplicationPipelineProfile.Server, registerQueries: false);
        Provider = services.BuildServiceProvider();
        var catalog = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS07.AddPayloads(catalog); ServerHandlerModuleS08.AddPayloads(catalog); ServerHandlerModuleS09.AddPayloads(catalog);
        var store = new CommandIntakeStore(Db, tenant);
        var runner = new ServerTransactionRunner(Db, tenant, new NoopTenantSessionInitializer());
        var uow = new CommandPlaneUnitOfWork(new ServerUnitOfWork(Db, runner), Db);
        Service = Intake.CreateService(catalog.Build(), new HandlerDispatcher(Provider), uow, store,
            new ServerSchedulingConcurrencyGuard(Db), new ClinicalAggregateVersionReader(Db), new ServerCommandEffectIdCollector(Db));
    }
    public CommandEnvelope Envelope(IJournaledCommand command)
    {
        var type = command.GetType();
        var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var values = ctor.GetParameters().ToDictionary(p => p.Name!, p => type.GetProperty(p.Name!,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!.GetValue(command));
        var json = JsonSerializer.Serialize(values, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return CommandEnvelope.Create(IdGenerator.NewId(), Intake.TenantId, Intake.UserId, Intake.DeviceId,
            command.CommandTypeName, command.AggregateTypeName, command.AggregateIdOrNull, json,
            CommandFingerprint.Compute(command.CommandTypeName, json), Intake.Clock.UtcNow, 1);
    }
    public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); await Db.DisposeAsync(); }
}
