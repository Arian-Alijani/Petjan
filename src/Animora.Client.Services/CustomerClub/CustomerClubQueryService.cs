using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Security;
using Animora.Application.UseCases.Finance;
using Animora.Client.Services.Statistics;
using Animora.Contracts.CustomerClub;
using Animora.Contracts.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Animora.Client.Services.CustomerClub;

/// <summary>
/// Dispatcher adapter for the «باشگاه مشتریان» screen: one scope per call, one dispatcher query per method,
/// Application read models mapped to Contracts DTOs. Authorization/validation live in the Application pipeline.
/// Failures surface as <see cref="StatisticsQueryException"/>.
/// </summary>
public sealed class CustomerClubQueryService(IServiceScopeFactory scopes) : ICustomerClubQueryService
{
    private const int MaxTake = 50;

    public Task<IReadOnlyList<BirthdayPet>> GetTodaysBirthdaysAsync(CancellationToken ct = default) =>
        QueryAsync(new TodaysBirthdaysQuery(),
            r => (IReadOnlyList<BirthdayPet>)r.Select(b =>
                new BirthdayPet(b.PetId, b.PetName, b.Species, b.OwnerId, b.OwnerName, b.Phone, b.AgeYears)).ToArray(), ct);

    public Task<IReadOnlyList<TopClient>> GetTopSpendersAsync(int take, CancellationToken ct = default) =>
        QueryAsync(new TopClientsQuery(Math.Clamp(take, 1, MaxTake)),
            r => (IReadOnlyList<TopClient>)r.Select(c => c.ToContract()).ToArray(), ct);

    public Task<IReadOnlyList<TopClient>> GetTopVisitorsAsync(int take, CancellationToken ct = default) =>
        QueryAsync(new TopVisitorsQuery(Math.Clamp(take, 1, MaxTake)),
            r => (IReadOnlyList<TopClient>)r.Select(c => c.ToContract()).ToArray(), ct);

    // Microsoft.Data.Sqlite runs async reads synchronously: keep the whole scoped call off the caller (UI) thread.
    private Task<TOut> QueryAsync<TRead, TOut>(IQuery<TRead> query, Func<TRead, TOut> map, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
                if (context.IsFailure) throw Failure(scope.ServiceProvider, context.Error.Code);
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                    .QueryAsync(query, context.Value!, ct).ConfigureAwait(false);
                if (result.IsFailure) throw Failure(scope.ServiceProvider, result.Error.Code);
                return map(result.Value!);
            }
            catch (OperationCanceledException) { throw; }
            catch (StatisticsQueryException) { throw; }
            catch (Exception ex) { throw Unexpected(ex); }
        }, ct);

    private StatisticsQueryException Unexpected(Exception ex)
    {
        using var scope = scopes.CreateScope();
        return Failure(scope.ServiceProvider, ErrorMapping.FromException(ex).Code);
    }

    private static StatisticsQueryException Failure(IServiceProvider services, string code)
    {
        var kind = StatisticsQueryException.Classify(code);
        var correlation = services.GetService<ICorrelationContext>()?.CurrentId ?? Guid.NewGuid().ToString("N");
        // Whitelisted category + code + opaque correlation only: no exception text, payload, identity or SQL in this log.
        services.GetService<ILogger<CustomerClubQueryService>>()?.LogWarning(
            "Customer club call failed: {FailureKind} {ErrorCode}; correlation {CorrelationId}", kind, code, correlation);
        return new StatisticsQueryException(kind, code, correlation);
    }
}
