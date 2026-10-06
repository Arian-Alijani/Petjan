using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Contracts.Cheques;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Cheques;

/// <summary>
/// The real <see cref="IChequesService"/> over the cheque use cases (same pattern as <c>InventoryQueryService</c>):
/// singleton, a fresh async scope per call resolving the scoped dispatcher and identity provider, no EF/SQL and no
/// business rule here — the state machine and counterparty invariant stay in the domain aggregate.
/// </summary>
public sealed class ChequesService(IServiceScopeFactory scopes) : IChequesService
{
    private const int MaxPageSize = 200; // QueryPaging.HardMaxPageSize

    public async Task<ChequePage> GetListAsync(ChequeListFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, zones, clock) = Resolve(scope);
        var zone = zones.GetClinicTimeZone();
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        var result = await QueryAsync(dispatcher, provider, new ChequeListQuery(
            filter.Direction?.ToDomain(), filter.Scope.ToApplication(), search, Math.Max(1, filter.Page),
            Math.Clamp(filter.PageSize, 1, MaxPageSize)), ct);

        var today = Animora.Application.Time.ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zone);
        var items = new List<ChequeItem>(result.Items.Count);
        foreach (var row in result.Items) items.Add(row.ToContract(zone, today));
        return new ChequePage(items, result.TotalCount);
    }

    public async Task<ChequeSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _, _) = Resolve(scope);
        return (await QueryAsync(dispatcher, provider, new ChequeSummaryQuery(), ct)).ToContract();
    }

    public async Task<ChequeDetail?> GetDetailAsync(string chequeId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, zones, clock) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await dispatcher.QueryAsync(new ChequeDetailQuery(chequeId), context.Value!, ct);
        if (result.IsFailure)
        {
            if (result.Error.Category == ErrorCode.NotFound) return null;
            throw new ServiceException(result.Error);
        }

        var zone = zones.GetClinicTimeZone();
        var today = Animora.Application.Time.ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zone);
        var detail = result.Value!;
        return new ChequeDetail(detail.Row.ToContract(zone, today),
            detail.History.Select(h => h.ToContract(zone)).ToArray());
    }

    public async Task<IReadOnlyList<ChequeParty>> FindOwnersAsync(string? search, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _, _) = Resolve(scope);
        var rows = await QueryAsync(dispatcher, provider, new ChequeOwnerLookupQuery(Clean(search)), ct);
        return rows.Select(r => r.ToContract()).ToArray();
    }

    public async Task<IReadOnlyList<ChequeParty>> FindSuppliersAsync(string? search, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _, _) = Resolve(scope);
        var rows = await QueryAsync(dispatcher, provider, new ChequeSupplierLookupQuery(Clean(search)), ct);
        return rows.Select(r => r.ToContract()).ToArray();
    }

    public async Task<string> RegisterAsync(NewChequeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The counterparty invariant is the domain's (CHEQUE_COUNTERPARTY_REQUIRED); failing it here, with the same
        // typed code, gives the form its friendly message instead of a generic validation failure.
        if (string.IsNullOrWhiteSpace(request.PartyId))
            throw new ServiceException(ErrorCodes.ChequeCounterpartyRequired);

        await using var scope = scopes.CreateAsyncScope();
        var (_, _, zones, _) = Resolve(scope);
        var zone = zones.GetClinicTimeZone();
        var due = ChequesMapping.ParseJalali(request.DueDate, zone)
            ?? throw new ServiceException(ErrorCodes.ValidationFailed);
        var issued = ChequesMapping.ParseJalali(request.IssueDate, zone)
            ?? throw new ServiceException(ErrorCodes.ValidationFailed);
        var inbound = request.Direction == ChequeDirectionDto.Inbound;
        return await SendAsync(scope, new RegisterChequeCommand(request.Direction.ToDomain(), request.Number,
            request.AmountMinor, due, issued, inbound ? request.PartyId : null, inbound ? null : request.PartyId,
            Clean(request.Bank), Clean(request.Branch), Clean(request.Account), Clean(request.SayadId),
            Clean(request.Note)));
    }

    public async Task UpdateAsync(string chequeId, UpdateChequeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopes.CreateAsyncScope();
        var (_, _, zones, _) = Resolve(scope);
        var due = ChequesMapping.ParseJalali(request.DueDate, zones.GetClinicTimeZone())
            ?? throw new ServiceException(ErrorCodes.ValidationFailed);
        _ = await SendAsync(scope, new UpdateChequeMetadataCommand(chequeId, request.Number, due, Clean(request.Bank),
            Clean(request.Branch), Clean(request.Account), Clean(request.SayadId), Clean(request.Note)));
    }

    public async Task DeleteAsync(string chequeId)
    {
        await using var scope = scopes.CreateAsyncScope();
        _ = await SendAsync(scope, new DeleteChequeDraftCommand(chequeId));
    }

    public async Task ApplyActionAsync(string chequeId, ChequeActionDto action, string? reason = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        _ = await SendAsync(scope, new TransitionChequeCommand(chequeId, action.ToApplication(), Clean(reason)));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<T> QueryAsync<T>(IDispatcher dispatcher, ICurrentUserProvider provider, IQuery<T> query,
        CancellationToken ct)
    {
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await dispatcher.QueryAsync(query, context.Value!, ct);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    private static async Task<T> SendAsync<T>(AsyncServiceScope scope, ICommand<T> command)
    {
        var (dispatcher, provider, _, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await dispatcher.SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider, IClinicTimeZoneProvider Zones, IClock Clock) Resolve(
        AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
         scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>(),
         scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>(),
         scope.ServiceProvider.GetRequiredService<IClock>());
}
