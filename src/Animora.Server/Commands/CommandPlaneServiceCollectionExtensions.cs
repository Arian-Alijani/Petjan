using Animora.Application;
using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Sync;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Transactions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Server.Commands;

internal static class CommandPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraCommandPlane(this IServiceCollection services,
        Action<ApplicationRegistrationBuilder>? configureHandlers = null,
        Action<CommandPayloadCatalogBuilder>? configurePayloads = null)
    {
        services.AddAnimoraApplication(configureHandlers, ApplicationPipelineProfile.Server, registerQueries: false);
        // AddAnimoraApplication also supplies client UI defaults. None belongs to this host: do not
        // satisfy their dependencies with a fake journal/session, or pull SQLite into server startup.
        services.RemoveAll<PendingCommandSummaryQueryHandler>();
        services.RemoveAll<UnsettledCommandListQueryHandler>();
        services.RemoveAll<ICurrentUserProvider>();
        services.RemoveAll<CapabilitySnapshotIssuer>();
        services.RemoveAll<CapabilityEvaluator>();
        services.Replace(ServiceDescriptor.Scoped<IAuthorizationGate, ServerPipelineAuthorizationGate>());
        services.Replace(ServiceDescriptor.Scoped<IEntitlementGate, ServerPipelineEntitlementGate>());
        services.TryAddScoped<IAuditWriter, ServerCommandAuditWriter>();
        services.TryAddScoped<ServerUnitOfWork>();
        services.Replace(ServiceDescriptor.Scoped<IUnitOfWork, CommandPlaneUnitOfWork>());
        services.TryAddScoped<ICommandIntakeStore, CommandIntakeStore>();
        services.TryAddScoped<CommandIntakeService>();
        services.TryAddScoped<IntakeRecoveryService>();
        services.TryAddScoped<CommandIntakeCompactor>();
        services.TryAddScoped<IAggregateVersionReader, NoAggregateVersionReader>();
        services.Replace(ServiceDescriptor.Scoped<IServerOutbox, CommandServerOutbox>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectCommittedProbe, NoProbe>());
        services.TryAddSingleton<RuleSetPolicy>();
        services.AddHttpContextAccessor();
        services.AddOptions<OfflineWindowOptions>().Validate(o => o.MaxOfflineDays is > 0 and <= 120).ValidateOnStart();
        services.AddOptions<CommandIntakeRetentionOptions>()
            .Validate(o => o.FullRecordDays >= 120 && o.ReceiptDays >= o.FullRecordDays).ValidateOnStart();
        var catalog = new CommandPayloadCatalogBuilder();
        configurePayloads?.Invoke(catalog);
        services.TryAddSingleton(catalog.Build());
        return services;
    }
}
