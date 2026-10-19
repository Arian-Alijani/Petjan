using Animora.Application.Ports;
using Animora.Persistence.PostgreSql.Repositories;
using Animora.Persistence.PostgreSql.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Persistence.PostgreSql.DependencyInjection;

public static class ClinicalPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraClinicalPersistence(this IServiceCollection services)
    {
        services.TryAddScoped<IOwnerRepository, OwnerRepository>();
        services.TryAddScoped<IPetRepository, PetRepository>();
        services.TryAddScoped<IWeightEntryRepository, WeightEntryRepository>();
        services.TryAddScoped<IDocumentReferenceRepository, DocumentReferenceRepository>();
        services.TryAddScoped<IMedicalHistoryRepository, MedicalHistoryRepository>();
        services.TryAddScoped<IReceptionQueueRepository, ReceptionQueueRepository>();
        services.TryAddScoped<IReceptionQueueQueries, ReceptionQueueQueries>();
        services.TryAddScoped<ITriageQueries, TriageQueries>();
        services.TryAddScoped<IVisitRepository, VisitRepository>();
        services.TryAddScoped<IVisitBillingHandoffWriter, VisitBillingHandoffWriter>();
        services.TryAddScoped<IVaccinationRepository, VaccinationRepository>();
        services.TryAddScoped<IVaccinationQueries, VaccinationQueries>();
        services.TryAddScoped<ITreatmentPlanRepository, TreatmentPlanRepository>();
        services.TryAddScoped<IClinicTimeZoneProvider, ServerClinicTimeZoneProvider>();
        return services;
    }
}
