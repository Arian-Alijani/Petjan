namespace Animora.Contracts.Cheques;

/// <summary>
/// Data seam of the «چک‌ها» screen. The ViewModel depends only on this interface; the real implementation lives in
/// <c>Animora.Client.Services</c> over the Application dispatcher. Backend failures surface as an
/// <see cref="InvalidOperationException"/> carrying a stable error code (mapped to Persian copy by the Desktop).
/// </summary>
public interface IChequesService
{
    /// <summary>One server-paged, filtered page of cheques.</summary>
    Task<ChequePage> GetListAsync(ChequeListFilter filter, CancellationToken ct = default);

    /// <summary>Open-portfolio totals (received / issued / bounced).</summary>
    Task<ChequeSummary> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>One cheque with its lifecycle history; <c>null</c> when it no longer exists.</summary>
    Task<ChequeDetail?> GetDetailAsync(string chequeId, CancellationToken ct = default);

    /// <summary>Owners (inbound drawer lookup) matching <paramref name="search"/>.</summary>
    Task<IReadOnlyList<ChequeParty>> FindOwnersAsync(string? search, CancellationToken ct = default);

    /// <summary>Suppliers (outbound payee lookup) matching <paramref name="search"/>.</summary>
    Task<IReadOnlyList<ChequeParty>> FindSuppliersAsync(string? search, CancellationToken ct = default);

    /// <summary>Registers a cheque; returns its id.</summary>
    Task<string> RegisterAsync(NewChequeRequest request);

    /// <summary>Edits a cheque that is still <c>Registered</c>.</summary>
    Task UpdateAsync(string chequeId, UpdateChequeRequest request);

    /// <summary>Deletes a cheque that was never filed.</summary>
    Task DeleteAsync(string chequeId);

    /// <summary>
    /// Applies one lifecycle action. <paramref name="reason"/> is mandatory for
    /// <see cref="ChequeActionDto.MarkBounced"/>, <see cref="ChequeActionDto.Return"/> and <see cref="ChequeActionDto.Cancel"/>.
    /// </summary>
    Task ApplyActionAsync(string chequeId, ChequeActionDto action, string? reason = null);
}
