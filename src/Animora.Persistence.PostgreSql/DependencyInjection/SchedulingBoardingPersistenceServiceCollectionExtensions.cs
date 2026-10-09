using Animora.Application.Ports;
using Animora.Persistence.PostgreSql.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Animora.Persistence.PostgreSql.DependencyInjection;

public static class SchedulingBoardingPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraSchedulingBoardingPersistence(this IServiceCollection services)
    {
        services.TryAddScoped<IAppointmentRepository, AppointmentRepository>();
        services.TryAddScoped<IAppointmentQueries, AppointmentQueries>();
        services.TryAddScoped<IAppointmentSettingsStore, AppointmentSettingsStore>();
        services.TryAddScoped<IBoardingRepository, BoardingRepository>();
        services.TryAddScoped<IBoardingQueries, BoardingQueries>();
        services.TryAddScoped<IBoardingServiceRepository, BoardingServiceRepository>();
        services.TryAddScoped<IRoomRepository, RoomRepository>();
        services.TryAddScoped<IBoardingBillingHandoffWriter, BoardingBillingHandoffWriter>();
        return services;
    }
}
