namespace Animora.Application.Ports;

/// <summary>
/// One fixed-expense template with its settlement for a single period (<see cref="PaidExpenseId"/> is
/// <see langword="null"/> while the period is unpaid). Due-state math is the Application layer's job.
/// </summary>
public sealed record RecurringExpenseRow(string Id, string CategoryCode, string Title, long AmountMinor,
    int DayOfMonth, bool IsActive, string? SupplierId, string? SupplierName, string? Note,
    string? PaidExpenseId, DateTimeOffset? PaidUtc);

/// <summary>Read side of fixed (recurring) expenses.</summary>
public interface IRecurringExpenseQueries
{
    /// <summary>
    /// Every template (active first, then by due day) joined with its payment for <paramref name="periodCode"/>.
    /// Bounded by the clinic's handful of fixed costs.
    /// </summary>
    Task<IReadOnlyList<RecurringExpenseRow>> GetForPeriodAsync(string periodCode, CancellationToken ct);
}
