using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Animora.Contracts.Messaging;

/// <summary>
/// Data seam for the «مدیریت پیامک» screen: remaining credit, usage charts, the
/// sent-SMS report, and the per-event settings (including the three vaccine
/// reminder modes).
/// <para>
/// DESIGN-ONLY: the production implementation (<c>Animora.Client.Services</c>) is a
/// stub that returns design defaults — it neither sends messages nor computes real
/// credit. It exists so the UI binds to a real interface and a future backend
/// (server-side send pipeline + credit accounting) can be dropped in behind it.
/// </para>
/// A cancelled call throws <see cref="System.OperationCanceledException"/>; any other
/// failure throws <see cref="SmsServiceException"/>.
/// </summary>
public interface ISmsService
{
    /// <summary>Remaining SMS credit (count and/or currency balance). Stub returns <see cref="SmsCredit.Unknown"/>.</summary>
    Task<SmsCredit> GetCreditAsync(CancellationToken ct = default);

    /// <summary>Usage roll-up for the overview charts over <paramref name="range"/>. Stub returns <see cref="SmsUsageReport.Empty"/>.</summary>
    Task<SmsUsageReport> GetUsageAsync(SmsReportRange range, CancellationToken ct = default);

    /// <summary>One page of sent-SMS rows, optionally filtered by <paramref name="eventType"/>. Stub returns <see cref="SentSmsPage.Empty"/>.</summary>
    Task<SentSmsPage> GetSentAsync(SmsReportRange range, SmsEventType? eventType, int page, int pageSize, CancellationToken ct = default);

    /// <summary>The clinic's current SMS settings. Stub returns <see cref="SmsSettings.Default"/>.</summary>
    Task<SmsSettings> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Persist edited settings. Stub is a no-op (nothing is wired yet).</summary>
    Task SaveSettingsAsync(SmsSettings settings, CancellationToken ct = default);
}
