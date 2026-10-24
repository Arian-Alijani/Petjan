using System.Globalization;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Client.Services.Payments;
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
/// <item><description><b>Failures are never disguised as success (D16).</b> Every mutation propagates a failed
/// <c>Result</c> as a typed <see cref="ServiceException"/> (as <see cref="EditLineAsync"/> always did), and every
/// read throws on failure rather than returning an empty page — an empty result means "no rows", never "the store
/// or the session is unavailable".</description></item>
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
        if (context.IsFailure) throw new ServiceException(context.Error);

        var result = await dispatcher.QueryAsync(new ReadyForBillingQuery(), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);

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
        ThrowOnFailedRead(result);

        var rows = result.Value?.Items ?? [];
        var list = new List<InvoiceSummary>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(rows[i].ToContract());
        return list;
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForOwnerAsync(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return [];
        var result = await QueryAsync(new OwnerInvoicesQuery(ownerId));
        ThrowOnFailedRead(result);

        var (_, zone) = Calendar();
        var rows = result.Value ?? [];
        var list = new List<InvoiceSummary>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(rows[i].ToContract(zone));
        return list;
    }

    public async Task<InvoiceDetail?> GetInvoiceAsync(string invoiceId)
    {
        var result = await QueryAsync(new InvoiceDetailQuery(invoiceId));
        ThrowOnFailedRead(result);
        // A successful query with no row means the id is unknown — that is a real answer, not a failure.
        if (result.Value is null) return null;

        var (_, zone) = Calendar();
        return result.Value.ToContract(zone);
    }

    public async Task<IReadOnlyList<InsurerRef>> GetInsurersAsync()
    {
        var result = await QueryAsync(new InsurersQuery());
        ThrowOnFailedRead(result);

        var rows = result.Value ?? [];
        var list = new List<InsurerRef>(rows.Count);
        for (var i = 0; i < rows.Count; i++) list.Add(rows[i].ToContract());
        return list;
    }

    public async Task<SaleKpiSnapshot> GetKpisAsync(IReadOnlyList<InvoiceSummary> invoices, int readyVisitCount)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        var (now, zone) = Calendar();
        var (from, to) = Animora.Client.Services.ClinicDayWindow.Of(now, zone);

        // Only the settled-today figure is a query of its own; the open receivables and the ready count come
        // from the lists the page has already loaded, so a refresh issues each underlying query once (X28).
        var kpi = await QueryAsync(new SaleKpiQuery(from, to));
        ThrowOnFailedRead(kpi);

        long openDue = 0;
        var openCount = 0;
        for (var i = 0; i < invoices.Count; i++)
        {
            // Contract Draft covers domain Draft+Posted; Partial is PartiallyPaid — the same "open" set as before.
            if (invoices[i].State is ContractInvoiceState.Draft or ContractInvoiceState.Partial)
            {
                openDue += invoices[i].Due;
                openCount++;
            }
        }

        var revenue = kpi.Value?.RevenueMinor ?? 0L;
        return new SaleKpiSnapshot(revenue, openDue, openCount, Math.Max(0, readyVisitCount));
    }

    public async Task<CatalogSnapshot> GetCatalogAsync()
    {
        var result = await QueryAsync(new SalesCatalogQuery());
        ThrowOnFailedRead(result);
        // A successful query with no snapshot is not a failure — the picker simply has nothing to offer.
        return result.Value?.ToContract() ?? new CatalogSnapshot([], [], []);
    }

    public async Task<CatalogItem?> FindCatalogItemByBarcodeAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;
        var result = await QueryAsync(new SalesCatalogByBarcodeQuery(barcode.Trim()));
        ThrowOnFailedRead(result);
        return result.Value?.ToContract();
    }

    public async Task<IReadOnlyList<OwnerRef>> GetOwnersAsync(string query)
    {
        var result = await QueryAsync(new OwnerLookupQuery(query));
        ThrowOnFailedRead(result);

        var rows = result.Value ?? [];
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
        var kind = SalesMapping.HistoryFilterKinds(filter.Kinds);

        var result = await QueryAsync(new SalesHistoryQuery(from, to, filter.Search, kind,
            filter.Sort.ToString(), filter.Page, filter.PageSize));
        ThrowOnFailedRead(result);

        var page = result.Value!;
        var rows = page.Page.Items;
        var items = new CashierHistoryEntry[rows.Count];
        for (var i = 0; i < rows.Count; i++) items[i] = rows[i].ToContract(zone);
        return new CashierHistoryPage(items, page.Page.TotalCount, page.InflowMinor, page.OutflowMinor);
    }

    // ── Draft lifecycle ─────────────────────────────────────────────────────

    public async Task<InvoiceDetail> CreateFromEncounterAsync(string encounterId)
    {
        // The queue unions visit and boarding handoffs; the handoff id goes to both commands, and the branch
        // that does not own that id returns NOT_FOUND, so exactly one create succeeds. ONLY a NotFound-category
        // failure may fall through to the other branch: any other failure (validation, permission, store) is the
        // real answer for this encounter and must be surfaced as-is instead of being replaced by a generic
        // "not found". Match on the NotFound *category* — the handoff handler returns the generic NOT_FOUND
        // (SalesHandoffFactory), not the invoice-specific INVOICE_NOT_FOUND, so comparing to a single literal code
        // wrongly threw for boarding-issued encounters instead of trying the stay branch.
        var visit = await SendAsync(new CreateInvoiceFromVisitCommand(encounterId));
        if (visit.IsSuccess) return await RequireDetailAsync(visit.Value!);
        if (visit.Error.Category != ErrorCode.NotFound) throw new ServiceException(visit.Error);

        var stay = await SendAsync(new CreateInvoiceFromStayCommand(encounterId));
        if (stay.IsSuccess) return await RequireDetailAsync(stay.Value!);
        throw new ServiceException(stay.Error);
    }

    private const string GuestSaleName = "مشتری مهمان";

    public async Task<InvoiceDetail> CreateBlankAsync()
    {
        // A blank counter sale starts as a guest; the picker then assigns a real owner.
        var created = await SendAsync(new CreateBlankInvoiceCommand(null, GuestSaleName, null));
        var id = created.IsSuccess ? created.Value : null;
        return await RequireDetailAsync(id ?? throw new InvalidOperationException("INVOICE_NOT_FOUND"));
    }

    public async Task<InvoiceDetail> AddLineAsync(string invoiceId, CatalogItem item, int quantity)
    {
        ArgumentNullException.ThrowIfNull(item);
        var sent = await SendAsync(new AddInvoiceLineCommand(invoiceId, item.Id, Math.Max(1, quantity)));
        if (sent.IsFailure) throw new ServiceException(sent.Error);
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail> AddCustomLineAsync(string invoiceId, CustomLineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sent = await SendAsync(new AddCustomInvoiceLineCommand(invoiceId, request.Title,
            Math.Max(0, request.UnitPrice), Math.Max(1, request.Quantity)));
        if (sent.IsFailure) throw new ServiceException(sent.Error);
        return await RequireDetailAsync(invoiceId);
    }

    // Line edits are addressed by the LineId the user saw, not a freshly re-derived index: the domain rejects a
    // mismatch with INVOICE_LINE_INDEX_STALE, which surfaces as a typed ServiceException (X27).
    public async Task<InvoiceDetail?> EditLineAsync(
        string invoiceId, int lineIndex, string lineId, int quantity, int discountPercent)
    {
        var sent = await SendAsync(new EditInvoiceLineCommand(invoiceId, lineIndex, lineId,
            Math.Max(1, quantity), Math.Clamp(discountPercent, 0, 100)));
        if (sent.IsFailure) throw new ServiceException(sent.Error);
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> RemoveLineAsync(string invoiceId, int lineIndex, string lineId)
    {
        var sent = await SendAsync(new RemoveInvoiceLineCommand(invoiceId, lineIndex, lineId));
        if (sent.IsFailure) throw new ServiceException(sent.Error);
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> SetDiscountAsync(string invoiceId, DiscountChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = request.Kind == DiscountKind.Amount ? DomainDiscountKind.Amount : DomainDiscountKind.Percent;
        var sent = await SendAsync(new SetInvoiceDiscountCommand(invoiceId, kind, Math.Max(0, request.Value)));
        if (sent.IsFailure) throw new ServiceException(sent.Error);
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> SetInsuranceAsync(string invoiceId, string? insurerId)
    {
        var sent = await SendAsync(new SetInvoiceInsuranceCommand(invoiceId,
            string.IsNullOrWhiteSpace(insurerId) ? null : insurerId));
        if (sent.IsFailure) throw new ServiceException(sent.Error);
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail> PickOwnerAsync(string invoiceId, OwnerPickResult pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (pick.IsGuest)
        {
            // Switching back to a walk-in (a wrong owner was picked) detaches the owner record.
            var sent = await SendAsync(new AssignInvoiceOwnerCommand(invoiceId, null, null, GuestSaleName));
            if (sent.IsFailure) throw new ServiceException(sent.Error);
        }
        else if (!string.IsNullOrWhiteSpace(pick.OwnerId))
        {
            var sent = await SendAsync(new AssignInvoiceOwnerCommand(invoiceId, pick.OwnerId, null));
            if (sent.IsFailure) throw new ServiceException(sent.Error);
        }
        return await RequireDetailAsync(invoiceId);
    }

    public async Task<InvoiceDetail?> DeleteInvoiceAsync(string invoiceId)
    {
        var current = await ReadModelAsync(invoiceId);
        if (current is null) return null;
        // Only a draft may be deleted; a posted invoice is immutable → null per the seam contract.
        if (current.Summary.State != DomainInvoiceState.Draft) return null;
        var sent = await SendAsync(new DeleteDraftInvoiceCommand(invoiceId));
        if (sent.IsFailure) throw new ServiceException(sent.Error);
        return null; // The row is tombstoned; the viewer refreshes the sidebar list.
    }

    // ── Payments and compensation ───────────────────────────────────────────

    public async Task<PaymentResult> TakePaymentAsync(string invoiceId, PaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // One shared pay-on-post / cap-to-due path for Sales and Cashier (X41).
        var outcome = await InvoicePaymentCore.PayAsync(invoiceId, InvoicePaymentCore.ToDomain(request.Method),
            request.Amount, null, command => SendAsync(command), () => ReadModelAsync(invoiceId),
            () => CashSessionAutoOpen.EnsureOpenAsync(scopes));
        if (outcome.Error == "INVOICE_NOT_FOUND") return new PaymentResult(false, outcome.Error, 0, false, null!);

        var updated = await RequireDetailAsync(invoiceId);
        return outcome.Ok
            ? new PaymentResult(true, null, outcome.Applied, updated.State == ContractInvoiceState.Settled, updated)
            : new PaymentResult(false, outcome.Error, 0, false, updated);
    }

    public async Task<InvoiceDetail?> VoidAsync(string invoiceId)
    {
        var current = await ReadModelAsync(invoiceId);
        if (current is null) return null;
        if (current.Summary.State is DomainInvoiceState.Void or DomainInvoiceState.Refunded) return null;

        var sent = await SendAsync(new VoidInvoiceCommand(invoiceId, DefaultReason));
        if (sent.IsFailure) throw new ServiceException(sent.Error);
        return await ReadDetailAsync(invoiceId);
    }

    public async Task<RefundResult> RefundAsync(string invoiceId, RefundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await InvoicePaymentCore.RefundAsync(invoiceId, request.Amount, DefaultReason,
            command => SendAsync(command), () => ReadModelAsync(invoiceId));
        if (outcome.Error == "INVOICE_NOT_FOUND") return new RefundResult(false, outcome.Error, null!);

        var updated = await RequireDetailAsync(invoiceId);
        return outcome.Ok
            ? new RefundResult(true, null, updated, outcome.Applied)
            : new RefundResult(false, outcome.Error, updated);
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

    /// <summary>
    /// D16: a failed READ is not an empty result. Swallowing the failure and returning an empty list (or
    /// <see langword="null"/>) made a broken store or an expired session look exactly like "no invoices" / "no
    /// insurers", and no caller could tell the two apart. The seam throws the typed <see cref="ServiceException"/>
    /// instead — the contract the sibling <c>PatientsService</c> (C01) already keeps, and the one the X40 callers
    /// already assume: they catch it, keep their last known state and say why (e.g. the cashier's ready-count, the
    /// sale-history grid). A successful query with no rows is still an ordinary empty result.
    /// </summary>
    private static void ThrowOnFailedRead<T>(Result<T> result)
    {
        if (result.IsFailure) throw new ServiceException(result.Error);
    }

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
