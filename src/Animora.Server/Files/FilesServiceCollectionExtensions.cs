using Animora.Application.Ports;
using Animora.Persistence.PostgreSql.Files;
using Animora.Server.Hosting;
namespace Animora.Server.Files;

internal static class FilesServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraFiles(this IServiceCollection services)
    {
        services.AddSingleton<FileStore>(); services.AddSingleton<ValetKeyService>();
        services.AddScoped<FileTransferService>(); services.AddScoped<IFileExistenceChecker, FileExistenceChecker>();
        services.AddSingleton<FileMaintenanceStore>(); services.AddSingleton<IFileStoreManifest, FileStoreManifest>();
        services.AddSingleton<IReadinessProbe, FilesRootProbe>(); services.AddHostedService<FileReconciliationWorker>();
        return services;
    }
}
