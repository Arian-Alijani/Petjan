using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Cashier;
using Animora.Domain.Sales;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Auth;
using Animora.Server.Signing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Server.Authorization;

/// <summary>DI wiring of the S05 tenant-context / authorization / entitlement / signing planes.</summary>
internal static class AuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tenant binding, stored-RBAC authorization, entitlement slot and snapshot signing services.
    /// Must run after <c>AddAnimoraIdentity</c> (it replaces that phase's tenant context). Deliberately does
    /// <b>not</b> call <c>AddAnimoraApplication()</c>: that would pull the whole use-case registry and dispatcher
    /// into the server — only the permission evaluator and its collaborators are registered, individually.
    /// </summary>
    public static IServiceCollection AddAnimoraAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Tenant context: request-bound from the stored membership; wins over the S04 AuthTenantContext binding.
        services.AddScoped<RequestTenantContext>();
        services.Replace(ServiceDescriptor.Scoped<ITenantContext>(sp => sp.GetRequiredService<RequestTenantContext>()));

        // Stored RBAC: server-owned PostgreSQL repositories + the shared Application evaluator and its rules.
        services.AddScoped<IStaffMembershipRepository, ServerStaffMembershipRepository>();
        services.AddScoped<IRoleDefinitionRepository, ServerRoleDefinitionRepository>();
        services.TryAddScoped<IResourceAuthorizationRule<Invoice>, InvoiceAuthorizationRule>();
        services.TryAddScoped<IResourceAuthorizationRule<CashSession>, CashSessionAuthorizationRule>();
        services.TryAddScoped<IPermissionEvaluator, PermissionEvaluator>();

        // Tenant binding + per-request state.
        services.AddScoped<IMembershipResolver, MembershipResolver>();
        services.AddScoped<ServerAuthorizationSubjectFactory>();
        services.AddScoped<IRequestAuthorizationState, RequestAuthorizationState>();

        // Policies: a permission-code policy provider + the fixed coarse set.
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ManagerOnlyHandler>();
        services.AddScoped<IAuthorizationHandler, DeviceActiveHandler>();
        services.AddScoped<IAuthorizationHandler, OperatorOnlyHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, ServerPermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ServerAuthorizationResultHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(CoarsePolicies.TenantMember, p => p.RequireAuthenticatedUser())
            .AddPolicy(CoarsePolicies.ManagerOnly, p => p.RequireAuthenticatedUser().AddRequirements(new ManagerOnlyRequirement()))
            .AddPolicy(CoarsePolicies.OperatorOnly, p => p.AddAuthenticationSchemes(Animora.Server.Operator.OperatorAuth.Scheme)
                .RequireAuthenticatedUser().AddRequirements(new OperatorOnlyRequirement()))
            .AddPolicy(CoarsePolicies.DeviceActive, p => p.RequireAuthenticatedUser().AddRequirements(new DeviceActiveRequirement()));

        services.AddScoped<ICommandAuthorizer, CommandAuthorizer>();

        // Entitlement slot (S16 replaces the gate).
        services.TryAddSingleton<IServerEntitlementGate, AllowAllServerEntitlementGate>();

        // Snapshot signing + device administration.
        services.AddScoped<SigningKeyStore>();
        services.AddScoped<ISnapshotSignerProvider>(sp => sp.GetRequiredService<SigningKeyStore>());
        services.AddScoped<DeviceAdminService>();

        return services;
    }
}
