using Animora.Domain.Results;

namespace Animora.Application.Ports;

/// <summary>
/// Pulls the vendor-authored messages the admin panel addressed to this clinic (<c>GET /notifications</c>) into the
/// local notification store and confirms real delivery to the server (<c>POST /notifications/receipts</c>).
/// <para>
/// Optional by design: when no server is configured no implementation is registered and the caller skips it, so the
/// offline experience is unchanged. It is a side channel — nothing it writes crosses into the command pipeline.
/// </para>
/// </summary>
public interface IPeerNotificationSync
{
    /// <summary>
    /// Fetches every message page past the stored cursor (bounded per call), upserts it into the local store by the
    /// server's stable id, advances the cursor in the same save, then reports the receipts. Returns the number of NEW
    /// messages materialised. A failure leaves the cursor where it was, so the next pass resumes exactly there.
    /// </summary>
    Task<Result<int>> PullAsync(CancellationToken ct = default);
}
