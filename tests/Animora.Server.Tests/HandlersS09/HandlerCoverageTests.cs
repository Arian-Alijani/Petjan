using Animora.Application;
using Animora.Application.Commands;
using Animora.Application.UseCases.Sales;
using Animora.Application.Dispatch;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Authorization;
using Animora.Server.Commands;
using Animora.Domain.Primitives;
using Animora.Server.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Server.Tests.HandlersS09;

public sealed class HandlerCoverageTests
{
    [Fact]
    public void EveryS09CommandType_HasHandler_CatalogEntry_AndPermissionRow()
    {
        var services = new ServiceCollection();
        services.AddAnimoraApplication(ServerHandlerModuleS09.AddHandlers, ApplicationPipelineProfile.Server, registerQueries: false);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var builder = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS09.AddPayloads(builder);
        var catalog = builder.Build();
        foreach (var wire in CommandTypes.All.Where(t => new[] { "sales.", "cashier." }.Any(p => t.StartsWith(p, StringComparison.Ordinal))))
        {
            if (ReservedCommandTypes.All.Contains(wire))
            {
                Assert.DoesNotContain(wire, catalog.Entries.Keys);
                continue;
            }
            Assert.True(catalog.Entries.TryGetValue(wire, out var candidates), wire);
            Assert.NotNull(CommandPermissionTable.RequiredFor(wire));
            foreach (var candidate in candidates!)
            {
                Assert.True(registry.TryGet(candidate.CommandRecord, out var handler));
                Assert.Equal(CommandPermissionTable.RequiredFor(wire), handler.PermissionCode);
            }
        }
        Assert.Equal(21, catalog.Entries.Count);
        Assert.Equal(2, catalog.Entries[CommandTypes.SalesServiceRateUpdate].Count);
    }

    // T2: every invoice command payload produced by the client (canonical, journaling keys stripped) must resolve in the server catalog.
    [Fact]
    public void InvoiceChainPayloads_ProducedByClient_ResolveInServerCatalog()
    {
        var builder = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS09.AddPayloads(builder);
        var catalog = builder.Build();
        var f = new Animora.Server.Tests.Commands.IntakeTestFixture();
        string inv = IdGenerator.NewId();
        (IJournaledCommand Command, string Type)[] chain =
        [
            (new CreateBlankInvoiceCommand("OWN1", null, "PET1"), CommandTypes.SalesInvoiceCreate),
            (new AddInvoiceLineCommand(inv, "CAT1", 2m), CommandTypes.SalesInvoiceLineAdd),
            (new AddCustomInvoiceLineCommand(inv, "Custom", 1000, 1m), CommandTypes.SalesInvoiceLineAdd),
            (new AssignInvoiceOwnerCommand(inv, "OWN1", "PET1", null), CommandTypes.SalesInvoiceOwnerSet),
            (new SetInvoiceDiscountCommand(inv, Animora.Domain.Sales.DiscountKind.Percent, 10m), CommandTypes.SalesInvoiceDiscountSet),
            (new SetInvoiceInsuranceCommand(inv, "INS1"), CommandTypes.SalesInvoiceInsuranceSet),
            (new PostInvoiceCommand(inv), CommandTypes.SalesInvoicePost),
        ];
        foreach (var (command, type) in chain)
        {
            string payload = CommandFingerprint.CanonicalJson((object)command);
            var envelope = CommandEnvelope.Create(IdGenerator.NewId(), f.TenantId, f.UserId, f.DeviceId, type, command.AggregateTypeName,
                command.AggregateIdOrNull, payload, CommandFingerprint.Compute(type, payload), f.Clock.UtcNow, 1);
            var resolved = catalog.Resolve(envelope);
            Assert.True(resolved.IsSuccess, $"{command.GetType().Name}: {resolved.Detail}");
            Assert.IsType(command.GetType(), resolved.Value!.Command);
        }
    }
}
