using System.Globalization;
using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Seed;

namespace Animora.Persistence.Sqlite.Sales;

/// <summary>
/// Allocates invoice numbers atomically from <c>LocalSequenceCounters</c>. The counter participates in
/// the ambient database transaction, so a failed posting cannot consume or expose a number.
/// </summary>
public sealed class InvoiceNumberAllocator(
    AnimoraLocalDbContext db,
    ISeedPeriodCodeProvider periodCodes) : IInvoiceNumberAllocator
{
    public async Task<string> AllocateAsync(
        string tenantId,
        DateTimeOffset issuedUtc,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var period = periodCodes.For(issuedUtc).PeriodCode;
        var separator = period.IndexOf('-');
        var jalaliYear = separator < 0 ? period : period[..separator];
        if (jalaliYear.Length != 4)
            throw new InvalidOperationException($"Invalid Jalali period code '{period}'.");

        var sequence = await PipelineSql.NextSequenceAsync(
            db,
            $"invoice:{tenantId}:{jalaliYear}",
            ct).ConfigureAwait(false);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{jalaliYear[^2..]}-{sequence:000000}");
    }
}
