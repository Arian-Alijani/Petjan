namespace Animora.Contracts.Sales;

/// <summary>Which catalogue a service rate belongs to.</summary>
public enum ServiceRateSource
{
    /// <summary>A clinic service billed from visits and the cashier.</summary>
    Clinic,

    /// <summary>A boarding add-on service billed from a stay.</summary>
    Boarding,
}

/// <summary>How a boarding service is charged (clinic services are always one-off).</summary>
public enum ServiceRateBilling
{
    PerStay,
    PerDay,
    PerUse,
}

/// <summary>One row of the «نرخ خدمات» list; <paramref name="Price"/> is in IRT.</summary>
public sealed record ServiceRate(
    string Id,
    ServiceRateSource Source,
    string Title,
    long Price,
    bool IsActive,
    ServiceRateBilling? Billing = null);

/// <summary>A service to add; <paramref name="Billing"/> only applies to <see cref="ServiceRateSource.Boarding"/>.</summary>
public sealed record NewServiceRate(
    ServiceRateSource Source,
    string Title,
    long Price,
    ServiceRateBilling Billing = ServiceRateBilling.PerUse);

/// <summary>
/// One row of the «پوشش بیمه‌ها» directory: the share of an invoice the insurer covers
/// (<paramref name="Percent"/>, 0–100) and the most it will ever pay (<paramref name="Ceiling"/> in IRT; <c>0</c> = no ceiling).
/// </summary>
public sealed record InsurerCoverage(string Id, string Name, decimal Percent, long Ceiling);

/// <summary>An insurer to add to the «پوشش بیمه‌ها» directory.</summary>
public sealed record NewInsurerCoverage(string Name, decimal Percent, long Ceiling);

/// <summary>
/// Data seam for the Sales «نرخ خدمات» tab: list, add and edit the priced services offered by the clinic and by
/// boarding, and manage the insurance-coverage directory that feeds every invoice's «پوشش بیمه» select.
/// Failures surface as exceptions carrying the error code.
/// </summary>
public interface IServiceRateService
{
    /// <summary>Every service (inactive ones included), clinic first, ordered by title.</summary>
    Task<IReadOnlyList<ServiceRate>> GetAsync();

    /// <summary>Adds a new service to the chosen catalogue.</summary>
    Task CreateAsync(NewServiceRate rate);

    /// <summary>Saves the title, price and active flag of an existing service.</summary>
    Task UpdateAsync(ServiceRate rate);

    /// <summary>Every insurer with its coverage percent and ceiling, ordered like the invoice select.</summary>
    Task<IReadOnlyList<InsurerCoverage>> GetInsurersAsync();

    /// <summary>Adds an insurer; it is immediately offered on invoices.</summary>
    Task CreateInsurerAsync(NewInsurerCoverage insurer);

    /// <summary>Changes an insurer's name, covered percent and ceiling (already-applied invoices keep their own percent).</summary>
    Task UpdateInsurerAsync(InsurerCoverage insurer);

    /// <summary>Removes an insurer from the directory; invoices that already used it are unchanged.</summary>
    Task DeleteInsurerAsync(string insurerId);
}
