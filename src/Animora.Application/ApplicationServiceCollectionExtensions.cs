using Animora.Application.Commands;
using Animora.Application.Correlation;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Diagnostics;
using Animora.Application.UseCases.Sync;
using Animora.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Animora.Domain.Cashier;
using Animora.Domain.Sales;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Application;

/// <summary>Single DI entry point of the use-case layer.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers correlation, <see cref="OperationIdFactory"/>, the security-plane defaults
    /// (scoped <see cref="PermissionAuthorizationGate"/> + <see cref="PermissionEvaluator"/> + the two typed resource
    /// rules (B04a), allow-all entitlement → E07, session-sourced <see cref="SessionCurrentUserProvider"/> (B03b);
    /// all <c>TryAdd</c> so replacements win), the command-pipeline defaults
    /// (<see cref="NullCommandTransport"/> → E06, <see cref="NullIntegrityAlertSink"/> → B05), the reference
    /// <see cref="PingQuery"/> pair and the two sync UI-seam queries, every
    /// explicit registration (identity use cases: <c>builder.AddIdentityUseCases()</c>, B03b) from
    /// <paramref name="configure"/>, then validates and freezes the handler registry.
    /// Throws <see cref="InvalidOperationException"/> on any registry gap (startup failure).
    /// </summary>
    public static IServiceCollection AddAnimoraApplication(this IServiceCollection services,
        Action<ApplicationRegistrationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICorrelationContext, CorrelationContext>();
        services.TryAddSingleton<OperationIdFactory>();
        services.TryAddScoped<IAuthorizationGate, PermissionAuthorizationGate>();
        services.TryAddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.TryAddScoped<IResourceAuthorizationRule<Invoice>, InvoiceAuthorizationRule>();
        services.TryAddScoped<IResourceAuthorizationRule<CashSession>, CashSessionAuthorizationRule>();
        services.TryAddSingleton<IEntitlementGate, AllowAllEntitlementGate>();
        services.TryAddSingleton<SessionContextCache>();
        services.TryAddScoped<ICurrentUserProvider, SessionCurrentUserProvider>();
        // B04b: offline capability issuer/evaluator (scoped: depend on the scoped store/role repository;
        // the signer port is a singleton registered by AddAnimoraLocalInfrastructure).
        services.TryAddScoped<CapabilitySnapshotIssuer>();
        services.TryAddScoped<CapabilityEvaluator>();
        // Command-pipeline defaults: replaced by E06 (transport) and B05 (integrity sink).
        services.TryAddSingleton<ICommandTransport, NullCommandTransport>();
        services.TryAddSingleton<IIntegrityAlertSink, NullIntegrityAlertSink>();

        var builder = new ApplicationRegistrationBuilder(services);
        builder.AddQuery<PingQuery, string, PingQueryHandler>();
        builder.AddQuery<PendingCommandSummaryQuery, PendingCommandSummary, PendingCommandSummaryQueryHandler>();
        builder.AddQuery<UnsettledCommandListQuery, PagedResult<PendingCommandRecord>, UnsettledCommandListQueryHandler>();
        configure?.Invoke(builder);

        var registry = builder.Build(Dispatcher.Chain);
        services.AddSingleton(registry);
        services.TryAddScoped<IDispatcher, Dispatcher>();
        return services;
    }
}
