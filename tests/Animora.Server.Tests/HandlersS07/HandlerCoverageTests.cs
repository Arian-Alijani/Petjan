using Animora.Application;
using Animora.Application.Dispatch;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Authorization;
using Animora.Server.Commands;
using Animora.Server.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Server.Tests.HandlersS07;

public sealed class HandlerCoverageTests
{
    [Fact]
    public void EveryS07CommandType_HasHandler_CatalogEntry_AndPermissionRow()
    {
        var services = new ServiceCollection();
        services.AddAnimoraApplication(ServerHandlerModuleS07.AddHandlers, ApplicationPipelineProfile.Server, registerQueries: false);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var builder = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS07.AddPayloads(builder);
        var catalog = builder.Build();
        foreach (var wire in CommandTypes.All.Where(t => new[] { "patients.", "reception.", "visits.", "vaccination." }.Any(p => t.StartsWith(p, StringComparison.Ordinal))))
        {
            Assert.DoesNotContain(wire, (IEnumerable<string>)ReservedCommandTypes.All);
            Assert.True(catalog.Entries.TryGetValue(wire, out var candidates), wire);
            Assert.NotNull(CommandPermissionTable.RequiredFor(wire));
            foreach (var candidate in candidates!)
            {
                Assert.True(registry.TryGet(candidate.CommandRecord, out var handler));
                Assert.Equal(CommandPermissionTable.RequiredFor(wire), handler.PermissionCode);
            }
        }
        Assert.Equal(2, catalog.Entries[CommandTypes.VisitsVisitStart].Count);
    }
}
