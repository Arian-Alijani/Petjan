using Animora.Application.Correlation;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Diagnostics;
using Animora.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Application;

/// <summary>Single DI entry point of the use-case layer.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers correlation, <see cref="OperationIdFactory"/>, the security-plane defaults
    /// (allow-all authorization → B04, allow-all entitlement → E07, <see cref="SingleUserProvider"/> → B03;
    /// all <c>TryAdd</c> so replacements win), the reference <see cref="PingQuery"/> pair, every explicit
    /// registration from <paramref name="configure"/>, then validates and freezes the handler registry.
    /// Throws <see cref="InvalidOperationException"/> on any registry gap (startup failure).
    /// </summary>
    public static IServiceCollection AddAnimoraApplication(this IServiceCollection services,
        Action<ApplicationRegistrationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICorrelationContext, CorrelationContext>();
        services.TryAddSingleton<OperationIdFactory>();
        services.TryAddSingleton<IAuthorizationGate, AllowAllAuthorizationGate>();
        services.TryAddSingleton<IEntitlementGate, AllowAllEntitlementGate>();
        services.TryAddScoped<ICurrentUserProvider, SingleUserProvider>();

        var builder = new ApplicationRegistrationBuilder(services);
        builder.AddQuery<PingQuery, string, PingQueryHandler>();
        configure?.Invoke(builder);

        var registry = builder.Build(Dispatcher.Chain);
        services.AddSingleton(registry);
        services.TryAddScoped<IDispatcher, Dispatcher>();
        return services;
    }
}
