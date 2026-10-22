using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Messaging;

namespace Animora.Client.Services.Messaging;

/// <summary>
/// DESIGN-ONLY stub implementation of <see cref="ISmsService"/>.
/// <para>
/// This exists purely so the «مدیریت پیامک» screen binds to a real Contracts seam.
/// It neither sends messages nor computes real credit; every read returns the
/// contract's design default (unknown credit / empty usage / empty report /
/// default settings) and the save is a no-op.
/// </para>
/// <para>
/// TODO(backend): replace the method bodies with dispatcher queries/commands over
/// <c>Animora.Application</c> once the server-side SMS send pipeline and credit
/// accounting exist. The shape returned here is the shape those real reads must
/// produce, so the UI needs no changes when the backend is wired.
/// </para>
/// Honours <see cref="CancellationToken"/> so the UI's per-visit cancellation keeps working.
/// </summary>
public sealed class SmsService : ISmsService
{
    public Task<SmsCredit> GetCreditAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(SmsCredit.Unknown);
    }

    public Task<SmsUsageReport> GetUsageAsync(SmsReportRange range, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(SmsUsageReport.Empty);
    }

    public Task<SentSmsPage> GetSentAsync(SmsReportRange range, SmsEventType? eventType, int page, int pageSize, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(SentSmsPage.Empty);
    }

    public Task<SmsSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(SmsSettings.Default);
    }

    public Task SaveSettingsAsync(SmsSettings settings, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // No backend yet: nothing is persisted. The future implementation dispatches a save command.
        return Task.CompletedTask;
    }
}
