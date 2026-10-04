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
/// Data seam for the Sales «نرخ خدمات» tab: list, add and edit the priced services offered by the clinic and by
/// boarding. Failures surface as exceptions carrying the error code.
/// </summary>
public interface IServiceRateService
{
    /// <summary>Every service (inactive ones included), clinic first, ordered by title.</summary>
    Task<IReadOnlyList<ServiceRate>> GetAsync();

    /// <summary>Adds a new service to the chosen catalogue.</summary>
    Task CreateAsync(NewServiceRate rate);

    /// <summary>Saves the title, price and active flag of an existing service.</summary>
    Task UpdateAsync(ServiceRate rate);
}
