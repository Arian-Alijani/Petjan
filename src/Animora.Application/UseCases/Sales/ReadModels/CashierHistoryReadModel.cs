using Animora.Application.Abstractions;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Sales.ReadModels;

public sealed record CashierHistoryReadModel(string Id, string InvoiceId, string Number,
    DateTimeOffset OccurredUtc, string Kind, long AmountMinor, string OwnerName,
    InvoiceState InvoiceState = InvoiceState.Draft, string? ActorUserId = null,
    string? ActorName = null, string? ActorRole = null);

public sealed record CashierHistoryPageReadModel(PagedResult<CashierHistoryReadModel> Page,
    long InflowMinor, long OutflowMinor);
