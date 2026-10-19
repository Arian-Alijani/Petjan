namespace Animora.Application.Ports;

/// <summary>
/// The opaque period code and the real UTC range of the accounting period that contains an instant.
/// </summary>
/// <remarks>
/// Mirrors the seed-time <c>SeedPeriod</c> shape so an auto-provisioned period is byte-for-byte
/// identical to one the first-run seeder would have produced for the same instant.
/// </remarks>
public readonly record struct AccountingPeriodBoundary(string PeriodCode, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>
/// Resolves the accounting period (opaque <c>yyyy-MM</c> Jalali code + its real UTC bounds) that
/// contains a given instant. Implemented in Infrastructure so the Application/Domain layers never
/// perform a Persian-calendar or host time-zone computation themselves (CONVENTIONS §2/§3.2).
/// </summary>
/// <remarks>
/// This is the <i>single</i> calendar authority used by on-demand period provisioning in
/// <see cref="Animora.Application.Finance.LedgerPostingHandler"/>. It must agree exactly with the
/// first-run seeder and the invoice/expense number allocators, which share the same provider, so
/// that a period created lazily at post time carries the identical code and boundaries.
/// </remarks>
public interface IAccountingPeriodBoundaryResolver
{
    /// <summary>The period whose half-open UTC range <c>[StartUtc, EndUtc)</c> contains <paramref name="occurredUtc"/>.</summary>
    AccountingPeriodBoundary Resolve(DateTimeOffset occurredUtc);
}
