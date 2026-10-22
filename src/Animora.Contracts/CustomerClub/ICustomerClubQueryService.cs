using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Statistics;

namespace Animora.Contracts.CustomerClub;

/// <summary>
/// Data seam for the «باشگاه مشتریان» screen (today's pet birthdays + client leaderboards). The production
/// implementation (Animora.Client.Services) reads the local SQLite store through the authorized query pipeline.
/// A cancelled call throws <see cref="System.OperationCanceledException"/>, any other failure throws
/// <see cref="StatisticsQueryException"/> (shared with the «آمار» seam). Sending greeting SMS is intentionally
/// not part of this seam yet.
/// </summary>
public interface ICustomerClubQueryService
{
    /// <summary>Active patients whose birthday is today in the clinic's time zone.</summary>
    Task<IReadOnlyList<BirthdayPet>> GetTodaysBirthdaysAsync(CancellationToken ct = default);

    /// <summary>The <paramref name="take"/> clients with the highest lifetime spend.</summary>
    Task<IReadOnlyList<TopClient>> GetTopSpendersAsync(int take, CancellationToken ct = default);

    /// <summary>The <paramref name="take"/> clients with the most visits (invoices).</summary>
    Task<IReadOnlyList<TopClient>> GetTopVisitorsAsync(int take, CancellationToken ct = default);
}
