using Animora.Application.Abstractions;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Sales.ReadModels;

public sealed record InvoiceSummaryReadModel(string Id, string Number, InvoiceKind Kind, InvoiceState State,
    string? OwnerId, string OwnerName, string PetName, string Phone, DateTimeOffset IssuedUtc,
    long TotalMinor, long DueMinor, SettlementStatus SettlementStatus);
