using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Sales;
using Animora.Contracts.Sales;
using Animora.Domain.Boarding;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Sales;

/// <summary>
/// The real <see cref="IServiceRateService"/> over the service-rate use cases. Singleton; the scoped
/// dispatcher and identity provider are resolved from a fresh async scope per call and never captured.
/// </summary>
public sealed class ServiceRateService(IServiceScopeFactory scopes) : IServiceRateService
{
    public async Task<IReadOnlyList<ServiceRate>> GetAsync()
    {
        var rows = await QueryAsync(new ServiceRatesQuery());
        return rows.Select(ToContract).ToArray();
    }

    public async Task CreateAsync(NewServiceRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        if (rate.Source == ServiceRateSource.Boarding)
            await SendAsync(new CreateBoardingServiceRateCommand(rate.Title.Trim(), rate.Price, ToDomain(rate.Billing)));
        else
            await SendAsync(new CreateServiceRateCommand(rate.Title.Trim(), rate.Price));
    }

    public async Task UpdateAsync(ServiceRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        var source = rate.Source == ServiceRateSource.Boarding ? ServiceRateSources.Boarding : ServiceRateSources.Clinic;
        await SendAsync(new UpdateServiceRateCommand(rate.Id, source, rate.Title.Trim(), rate.Price, rate.IsActive));
    }

    public async Task<IReadOnlyList<InsurerCoverage>> GetInsurersAsync()
    {
        var rows = await QueryAsync(new InsurersQuery());
        return rows.Select(r => new InsurerCoverage(r.Id, r.Name, r.CoveragePercent, r.CeilingMinor)).ToArray();
    }

    public async Task CreateInsurerAsync(NewInsurerCoverage insurer)
    {
        ArgumentNullException.ThrowIfNull(insurer);
        await SendAsync(new CreateInsurerCommand(insurer.Name.Trim(), insurer.Percent, insurer.Ceiling));
    }

    public async Task UpdateInsurerAsync(InsurerCoverage insurer)
    {
        ArgumentNullException.ThrowIfNull(insurer);
        await SendAsync(new UpdateInsurerCommand(insurer.Id, insurer.Name.Trim(), insurer.Percent, insurer.Ceiling));
    }

    public async Task DeleteInsurerAsync(string insurerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(insurerId);
        await SendAsync(new DeleteInsurerCommand(insurerId));
    }

    private static ServiceRate ToContract(ServiceRateReadModel row) => new(row.Id,
        row.Source == ServiceRateSources.Boarding ? ServiceRateSource.Boarding : ServiceRateSource.Clinic,
        row.Title, row.PriceMinor, row.IsActive,
        Enum.TryParse<ServiceRateBilling>(row.Billing, out var billing) ? billing : null);

    private static BoardingServiceBilling ToDomain(ServiceRateBilling billing) => billing switch
    {
        ServiceRateBilling.PerStay => BoardingServiceBilling.PerStay,
        ServiceRateBilling.PerDay => BoardingServiceBilling.PerDay,
        _ => BoardingServiceBilling.PerUse,
    };

    private async Task<T> QueryAsync<T>(IQuery<T> query)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    private async Task<T> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>()
            .BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }
}
