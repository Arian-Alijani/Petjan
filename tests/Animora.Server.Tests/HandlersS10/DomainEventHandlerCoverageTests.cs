using Animora.Application.Abstractions;
using Animora.Application.Finance;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Animora.Server.Tests.HandlersS10;
public sealed class DomainEventHandlerCoverageTests
{
    [Fact] public void EveryRaisedEvent_HasServerHandlerWhereClientHas()
    {
        var services = new ServiceCollection(); services.AddAnimoraServerFinancialEvents();
        var expected = typeof(LedgerPostingHandler).GetInterfaces().Where(i => i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)).ToArray();
        Assert.Equal(10, expected.Length);
        foreach (var contract in expected) Assert.Contains(services, d => d.ServiceType == contract);
        Assert.Contains(services, d => d.ServiceType == typeof(IDomainEventDispatcher) && d.ImplementationType == typeof(ServerDomainEventDispatcher));
        Assert.DoesNotContain(services, d => d.ImplementationType?.Name == "FinanceProjectionHandler");
    }
}
