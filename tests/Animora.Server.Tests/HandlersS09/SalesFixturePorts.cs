using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Animora.Application.Behaviors;
using Animora.Application.Ports;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Server.Tests.HandlersS09;

internal sealed class SalesInvoiceRepository(params Invoice[] initial) : IInvoiceRepository
{
    private readonly Dictionary<string, Invoice> _invoices = initial.ToDictionary(i => i.Id);
    public Task<Invoice?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(_invoices.GetValueOrDefault(id));
    public Task<Invoice> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(_invoices[id]);
    public void Add(Invoice entity) => _invoices[entity.Id] = entity;
    public void AddRange(IEnumerable<Invoice> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}

internal sealed class SalesPaymentRepository : IPaymentRepository
{
    public List<Payment> Added { get; } = [];
    public Task<Payment?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Added.FirstOrDefault(p => p.Id == id));
    public Task<Payment> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Added.First(p => p.Id == id));
    public Task<Payment?> FindLatestForInvoiceAsync(string invoiceId, CancellationToken ct = default) =>
        Task.FromResult(Added.LastOrDefault(p => p.InvoiceId == invoiceId && p.Amount.Minor > 0));
    public void Add(Payment entity) => Added.Add(entity);
    public void AddRange(IEnumerable<Payment> entities) => Added.AddRange(entities);
}

internal sealed class SalesNumberAllocator : IInvoiceNumberAllocator
{
    public int Allocations { get; private set; }
    public Task<string> AllocateAsync(string tenantId, DateTimeOffset issuedUtc, CancellationToken ct = default) =>
        Task.FromResult($"05-{++Allocations:000000}");
}

internal sealed class SalesPostingFake : ISalesPostingData
{
    public bool AllowNegativeStock { get; set; } = true;
    public long OnHand { get; set; } = 10_000;
    public HandoffPostStatus Status { get; set; } = HandoffPostStatus.Ready;
    public string? InvoicedInvoiceId { get; private set; }
    public Task<BoardingHandoffKind?> GetReadyBoardingHandoffKindAsync(string stayId, CancellationToken ct) =>
        Task.FromResult<BoardingHandoffKind?>(null);
    public Task<long> GetOnHandScaledAsync(string productId, string variantId, CancellationToken ct) => Task.FromResult(OnHand);
    public Task<Money> GetLatestReceiptCostAsync(string productId, string variantId, CancellationToken ct) => Task.FromResult(Money.Irt(500));
    public Task<HandoffPostStatus> GetHandoffStatusAsync(string source, string sourceId, CancellationToken ct) => Task.FromResult(Status);
    public Task<HandoffPostStatus> MarkHandoffInvoicedAsync(string source, string sourceId, string invoiceId, CancellationToken ct)
    {
        var before = Status;
        if (before == HandoffPostStatus.Ready)
        {
            Status = HandoffPostStatus.AlreadyInvoiced;
            InvoicedInvoiceId = invoiceId;
        }
        return Task.FromResult(before);
    }
}
