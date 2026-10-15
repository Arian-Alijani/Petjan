using Animora.Domain.Results;

namespace Animora.Application.Ports;

/// <summary>
/// D04b — pulls the server-authored financial read state (peer invoices/payments from <c>GET /finance/ledger</c>) into the
/// local read-only cache. Strictly a display feed: nothing it writes is ever read by the command pipeline.
/// <para>
/// Optional by design: when no server is configured no implementation is registered and the caller skips it, so the
/// offline experience is byte-for-byte unchanged.
/// </para>
/// </summary>
public interface IPeerFinanceSync
{
    /// <summary>
    /// Fetches every ledger page past the stored cursor (bounded per call) and upserts it. Returns the number of
    /// invoice + payment rows applied. A failure leaves the cursor where it was, so the next pass resumes exactly there.
    /// </summary>
    Task<Result<int>> PullAsync(CancellationToken ct = default);
}
