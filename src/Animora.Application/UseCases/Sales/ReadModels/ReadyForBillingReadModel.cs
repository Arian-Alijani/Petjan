using Animora.Application.Abstractions;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Sales.ReadModels;

public sealed record ReadyForBillingReadModel(string Id, string Source, string OwnerId, string OwnerName,
    string PetId, string PetName, string DoctorId, DateTimeOffset CompletedUtc, string LinesJson);
