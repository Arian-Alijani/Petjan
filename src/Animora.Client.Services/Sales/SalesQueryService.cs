using System.Globalization;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Contracts.Sales;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using ContractInvoiceState = Animora.Contracts.Sales.InvoiceState;
using DomainDiscountKind = Animora.Domain.Sales.DiscountKind;
using DomainInvoiceState = Animora.Domain.Sales.InvoiceState;

namespace Animora.Client.Services.Sales;

/// <summary>
/// The real <see cref="ISalesQueryService"/> over the C07 use cases (the C01 pattern: <c>PatientsService</c>).
/// Singleton; the scoped dispatcher, identity provider and read ports are resolved from a fresh async scope
/// per call and never captured. This class holds <b>no EF / SQL / business rule</b> — it builds a
/// query/command context, dispatches the use case, and maps the result to Contracts.
/// </summary>
/// <remarks>
/// <para><b>Posting-moment design decisions (documented, not reshaped).</b> The Contracts surface encodes
/// three deliberate behaviours:</para>
/// <list type="bullet">
/// <item><description><b>Pay-on-post.</b> <see cref="TakePaymentAsync"/> posts the invoice first when it is
/// still a <c>Draft</c> (one logical payment operation: post + register), then registers the payment, so a
/// draft invoice becomes a real posted document the moment money moves.</description></item>
/// <item><description><b>Void-as-reversal.</b> <see cref="VoidAsync"/> never deletes: a posted invoice is
/// reversed through <c>VoidInvoiceCommand</c> (the domain appends a compensating adjustment); a draft is
/// voided directly. State-committed invoices return <c>null</c>.</description></item>
/// <item><description><b>Draft-only delete.</b> <see cref="DeleteInvoiceAsync"/> is allowed only for a
/// <c>Draft</c> (<c>DeleteDraftInvoiceCommand</c>); a posted invoice maps to <c>null</c>.</description></item>
/// </list>
/// </remarks>
public sealed class SalesQueryService(IServiceScopeFactory scopes) : ISalesQueryService
{
    private const int MaxPageSize = 200; // QueryPaging.HardMaxPageSize.
    private const string DefaultReason = "عملیات صندوق"; // Persian reason recorded on compensating commands.

    // ── Reads ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ReadyEncounterRef>> GetReadyEncountersAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, zones) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return [];

        var result = await dispatcher.QueryAsync(new ReadyForBillingQuery(), context.Value!);
        if (result.IsFailure) return [];

        var zone = zones.GetClinicTimeZone();
        var rows = result.Value!;
        var list = new List<ReadyEncounterRef>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(rows[i].ToContract(zone));
        return list;
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesAsync()
    {
        // Unpaged Contracts member → page 1 at the hard max (documented; CONVENTIONS §10 item 2).
        var result = await QueryAsync(new InvoiceListQuery(null, 1, MaxPageSize));
        if (result.IsFailure || result.Value is null) return [];

        var rows = result.Value.Items;
        var list = new List<InvoiceSummary>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(rows[i].ToContract());
        return list;
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForOwnerAsync(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return [];
        var result = await QueryAsync(new OwnerInvoicesQuery(ownerId));
        if (result.IsFailure || result.Value is null) return [];

        var (_, zone) = Calendar();
        var rows = result.Value;
        var list = new List<InvoiceSummary>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(rows[i].ToContract(zone));
        return list;
    }

    public async Task<InvoiceDetail?> GetInvoiceAsync(string invoiceId)
    {
        var result = await QueryAsync(new InvoiceDetailQuery(invoiceId));
        if (result.IsFailure || result.Value is null) return null;
        var (_, zone) = Calendar();
        return result.Value.ToContract(zone);
    }

    public async Task<IReadOnlyList<InsurerRef>> GetInsurersAsync()
    {
        var result = await QueryAsync(new InsurersQuery());
        if (result.IsFailure || result.Value is null) return [];
        var rows = result.Value;
        var list = new List<InsurerRef>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(rows[i].ToContract());
        return list;
    }

    public async Task<SaleKpiSnapshot> GetKpisAsync()
    {
        var (now, zone) = Calendar();
        var (from, to) = Animora.Client.Services.ClinicDayWindow.Of(now, zone);

        var kpi = await QueryAsync(new SaleKpiQuery(from, to));
        var open = await QueryAsync(new InvoiceListQuery(null, 1, MaxPageSize));
        var ready = await QueryAsync(new ReadyForBillingQuery());

        long openDue = 0;
        var openCount = 0;
        if (open.IsSuccess && open.Value is not null)
        {
            foreach (var invoice in open.Value.Items)
            {
                if (invoice.State is DomainInvoiceState.Draft or DomainInvoiceState.Posted
                    or DomainInvoiceState.PartiallyPaid)
                {
                    openDue += invoice.DueMinor;
                    openCount++;
                }
            }
        }

        var revenue = kpi.IsSuccess && kpi.Value is not null ? kpi.Value.RevenueMinor : 0L;
        var readyCount = ready.IsSuccess && ready.Value is not null ? ready.Value.Count : 0;
        return new SaleKpiSnapshot(revenue, openDue, openCount, readyCount);
    }

    public async Task<CatalogSnapshot> GetCatalogAsync()
    {
        var result = await QueryAsync(new SalesCatalogQuery());
        return result.IsSuccess && result.Value is not null
            ? result.Value.ToContract() : new CatalogSnapshot([], [], []);
    }

    public async Task<IReadOnlyList<OwnerRef>> GetOwnersAsync(string query)
    {
        var result = await QueryAsync(new OwnerLookupQuery(query));
        if (result.IsFailure || result.Value is null) return [];
        var rows = result.Value;
        var list = new List<OwnerRef>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(rows[i].ToContract());
        return list;
    }

    public async Task<CashierHistoryPage> GetHistoryAsync(CashierHistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (_, zone) = Calendar();
        var from = Animora.Client.Services.ClinicDayWindow.ParseJalali(filter.FromDate, zone, endOfDay: false);
        var to = Animora.Client.Services.ClinicDayWindow.ParseJalali(filter.ToDate, zone, endOfDay: true);
        var kind = filter.Kinds.Count == 1 ? SalesMapping.HistoryFilterKind(filter.Kinds.First()) : null;

        var result = await QueryAsync(new SalesHistoryQuery(from, to, filter.Search, kind,
            filter.Sort.ToString(), filter.Page, filter.PageSize));
        if (result.IsFailure || result.Value is null) return new CashierHistoryPage([], 0, 0, 0);

        var page = result.Value;
        var rows = page.Page.Items;
        var items = new CashierHistoryEntry[rows.Count];
        for (var i = 0; i < rows.Count; i++) items[i] = rows[i].ToContract(zone);
        return new CashierHistoryPage(items, page.Page.TotalCount, page.InflowMinor, page.OutflowMinor);
    }

    // ── Draft lifecycle ─────────────────────────────────────────────────────

    public async Task<InvoiceDetail> CreateFromEncounterAsync(string encounterId)
    {
        // The queue unions visit and boarding handoffs; the handoff id goes to both commands, and the branch
        // that does not own that id returns NOT_FOUND, so exactly one create succeeds.
        var visit = await SendAsync(new CreateInvoiceFromVisitCommand(encounterId));
        var id = visit.IsSuccess ? visit.Value : null;
        if (string.IsNullOrEmpty(id))
        {
            var stay = await SendAsync(new CreateInvoiceFromStayCommand(encounterId));
            id = stay.IsSuccess ? stay.Value : null;
        }
        return await RequireDetailAsync(id ?? throw new InvalidOperationException("INVOICE_NOT_FOUND"));
    }

    public async Task<InvoiceDetail> CreateBlankAsync()
    {
        // A blank counter sale starts as a guest; the picker then assigns a real owner.
        var created = await SendAsync(new CreateBlankInvoiceCommand(null, "مشتری مهمان", null));
        var id = created.IsSuccess ? created.Value : null;
        return await RequireDetailAsync(id ?? throw new InvalidOperationException("INVOICE_NOT_FOUND"));
    }

    public async Task<InvoiceDetail> AddLineAsync(string invoiceId, CatalogItem item, int quantity)
    {
        ArgumentNullException.ThrowIfNull(item);
        _ = await SendAsync(new AddInvoiceLineCommand(invoiceId, item.Id, Math.Max(1, quantity)));
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail> AddCustomLineAsync(string invoiceId, CustomLineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await SendAsync(new AddCustomInvoiceLineCommand(invoiceId, request.Title,
            Math.Max(0, request.UnitPrice), Math.Max(1, request.Quantity)));
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> EditLineAsync(string invoiceId, int lineIndex, int quantity, int discountPercent)
    {
        var current = await ReadModelAsync(invoiceId);
        var line = LineAt(current, lineIndex);
        if (line is null) return null;
        _ = await SendAsync(new EditInvoiceLineCommand(invoiceId, lineIndex, line.LineId,
            Math.Max(1, quantity), Math.Clamp(discountPercent, 0, 100)));
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> RemoveLineAsync(string invoiceId, int lineIndex)
    {
        var current = await ReadModelAsync(invoiceId);
        var line = LineAt(current, lineIndex);
        if (line is null) return null;
        _ = await SendAsync(new RemoveInvoiceLineCommand(invoiceId, lineIndex, line.LineId));
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> SetDiscountAsync(string invoiceId, DiscountChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = request.Kind == DiscountKind.Amount ? DomainDiscountKind.Amount : DomainDiscountKind.Percent;
        _ = await SendAsync(new SetInvoiceDiscountCommand(invoiceId, kind, Math.Max(0, request.Value)));
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> SetInsuranceAsync(string invoiceId, string? insurerId)
    {
        _ = await SendAsync(new SetInvoiceInsuranceCommand(invoiceId,
            string.IsNullOrWhiteSpace(insurerId) ? null : insurerId));
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail> PickOwnerAsync(string invoiceId, OwnerPickResult pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (!pick.IsGuest && !string.IsNullOrWhiteSpace(pick.OwnerId))
        {
            _ = await SendAsync(new AssignInvoiceOwnerCommand(invoiceId, pick.OwnerId, null));
        }
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> DeleteInvoiceAsync(string invoiceId)
    {
        var current = await ReadModelAsync(invoiceId);
        if (current is null) return null;
        // Only a draft may be deleted; a posted invoice is immutable → null per the seam contract.
        if (current.Summary.State != DomainInvoiceState.Draft) return null;
        _ = await SendAsync(new DeleteDraftInvoiceCommand(invoiceId));
        return null; // The row is tombstoned; the viewer refreshes the sidebar list.
    }

    // ── Payments and compensation ───────────────────────────────────────────

    public async Task<PaymentResult> TakePaymentAsync(string invoiceId, PaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await ReadModelAsync(invoiceId);
        if (current is null) return new PaymentResult(false, "INVOICE_NOT_FOUND", 0, false, null!);

        if (current.Summary.State is DomainInvoiceState.Void or DomainInvoiceState.Refunded)
        {
            return new PaymentResult(false, "INVOICE_IMMUTABLE", 0, false, await RequireDetailAsync(invoiceId));
        }

        // Pay-on-post: a draft becomes a real posted document the moment money moves (one logical op).
        if (current.Summary.State == DomainInvoiceState.Draft)
        {
            var posted = await SendAsync(new PostInvoiceCommand(invoiceId));
            if (posted.IsFailure)
            {
                return new PaymentResult(false, "INVOICE_NOT_POSTED", 0, false, await RequireDetailAsync(invoiceId));
            }

            // Posting can finalise the payable total (tax / rounding / insurance), so the cap below must use the
            // POSTED due, not the pre-post draft figure (X17).
            current = await ReadModelAsync(invoiceId) ?? current;
        }

        var due = current.Summary.DueMinor;
        var amount = Math.Min(Math.Max(0, request.Amount), Math.Max(0, due));
        if (amount <= 0)
        {
            return new PaymentResult(false, "PAYMENT_EXCEEDS_DUE", 0, false, await RequireDetailAsync(invoiceId));
        }

        var sent = await SendAsync(new RegisterPaymentCommand(invoiceId, request.Method.ToDomain(),
            Money.Irt(amount)));
        if (sent.IsFailure)
        {
            return new PaymentResult(false, "PAYMENT_FAILED", 0, false, await RequireDetailAsync(invoiceId));
        }

        var updated = await RequireDetailAsync(invoiceId);
        return new PaymentResult(true, null, amount, updated.State == ContractInvoiceState.Settled, updated);
    }

    public async Task<InvoiceDetail?> VoidAsync(string invoiceId)
    {
        var current = await ReadModelAsync(invoiceId);
        if (current is null) return null;
        if (current.Summary.State is DomainInvoiceState.Void or DomainInvoiceState.Refunded) return null;

        _ = await SendAsync(new VoidInvoiceCommand(invoiceId, DefaultReason));
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<RefundResult> RefundAsync(string invoiceId, RefundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await ReadModelAsync(invoiceId);
        if (current is null) return new RefundResult(false, "INVOICE_NOT_FOUND", null!);
        if (current.Summary.State is not (DomainInvoiceState.PartiallyPaid or DomainInvoiceState.Paid))
        {
            return new RefundResult(false, "INVOICE_IMMUTABLE", await RequireDetailAsync(invoiceId));
        }

        var amount = Math.Min(Math.Max(0, request.Amount), current.PaidMinor);
        if (amount <= 0)
        {
            return new RefundResult(false, "ADJUSTMENT_EXCEEDS_PAID", await RequireDetailAsync(invoiceId));
        }

        var sent = await SendAsync(new RefundInvoiceCommand(invoiceId, Money.Irt(amount), DefaultReason));
        if (sent.IsFailure)
        {
            return new RefundResult(false, "REFUND_FAILED", await RequireDetailAsync(invoiceId));
        }
        return new RefundResult(true, null, await RequireDetailAsync(invoiceId), amount);
    }

    public async Task<LineReturnResult> ReturnLineAsync(string invoiceId, LineReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await ReadModelAsync(invoiceId);
        if (current is null) return new LineReturnResult(false, "INVOICE_NOT_FOUND", 0, null!);
        var line = LineAt(current, request.LineIndex);
        if (line is null)
        {
            return new LineReturnResult(false, "INVOICE_LINE_INDEX_STALE", 0, await RequireDetailAsync(invoiceId));
        }

        var sent = await SendAsync(new ReturnInvoiceLineCommand(invoiceId, line.LineId, DefaultReason));
        if (sent.IsFailure)
        {
            return new LineReturnResult(false, "RETURN_FAILED", 0, await RequireDetailAsync(invoiceId));
        }

        // A line return credits the line net; the drawer refund equals that credit note.
        var credited = (long)Math.Round(line.UnitPriceMinor * line.Quantity, MidpointRounding.AwayFromZero);
        return new LineReturnResult(true, null, credited, await RequireDetailAsync(invoiceId));
    }

    // ── Dispatch helpers ────────────────────────────────────────────────────

    private async Task<Result<T>> QueryAsync<T>(IQuery<T> query)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return Result<T>.Fail(context.Error, context.Detail);
        return await dispatcher.QueryAsync(query, context.Value!);
    }

    private async Task<Result<T>> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) return Result<T>.Fail(context.Error, context.Detail);
        return await dispatcher.SendAsync(command, context.Value!);
    }

    private async Task<InvoiceDetailReadModel?> ReadModelAsync(string invoiceId)
    {
        var result = await QueryAsync(new InvoiceDetailQuery(invoiceId));
        return result.IsSuccess ? result.Value : null;
    }

    private async Task<InvoiceDetail?> ReadDetailAsync(string invoiceId)
    {
        var model = await ReadModelAsync(invoiceId);
        if (model is null) return null;
        var (_, zone) = Calendar();
        return model.ToContract(zone);
    }

    private async Task<InvoiceDetail> RequireDetailAsync(string invoiceId) =>
        await ReadDetailAsync(invoiceId) ?? throw new InvalidOperationException("INVOICE_NOT_FOUND");

    private static InvoiceLineReadModel? LineAt(InvoiceDetailReadModel? detail, int index)
    {
        if (detail is null || index < 0 || index >= detail.Lines.Count) return null;
        return detail.Lines[index];
    }

    private (DateTimeOffset Now, TimeZoneInfo Zone) Calendar()
    {
        using var scope = scopes.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var zone = scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
        return (clock.UtcNow, zone);
    }

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider, IClinicTimeZoneProvider Zones) Resolve(
        AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
         scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>(),
         scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>());
}
