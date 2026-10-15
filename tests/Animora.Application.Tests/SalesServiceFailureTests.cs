using System;
using System.Threading;
using System.Threading.Tasks;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Client.Services;
using Animora.Client.Services.Sales;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// D16: the sales seam must not disguise a store/session failure as an empty result. A failed read throws the typed
/// <see cref="ServiceException"/> (an empty page means "no rows", nothing else), and a failed mutation propagates
/// its failed <see cref="Result{T}"/> instead of returning the unchanged invoice as if it had succeeded. The X40
/// callers already assume this contract — e.g. the cashier's ready-count and the sale-history grid both catch it
/// and keep their last known state.
/// </summary>
public sealed class SalesServiceFailureTests
{
    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class QueryOnlyUserProvider : ICurrentUserProvider
    {
        public Result<QueryContext> BuildQueryContext() => Result<QueryContext>.Ok(new QueryContext("T-1", "U-1", "COR-1"));
        public QueryContext BuildAnonymousQueryContext() => BuildQueryContext().Value!;
        public Result<CommandContext> BuildAnonymousCommandContext(string? operationId = null) => throw new NotSupportedException();
        public Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
            long? expectedVersion, CancellationToken ct) =>
            Task.FromResult(Result<CommandContext>.Ok(new CommandContext("T-1", "U-1", "D-1",
                operationId ?? "OP-1", "COR-1", null, dependsOn, expectedVersion, DateTimeOffset.UnixEpoch)));
    }

    /// <summary>Every dispatch fails the way a broken store / expired session does.</summary>
    private sealed class UnavailableDispatcher : IDispatcher
    {
        public Task<Result<T>> SendAsync<T>(ICommand<T> command, CommandContext ctx, CancellationToken ct = default) =>
            Task.FromResult(Result<T>.Fail(ErrorCodes.StoreUnavailable));

        public Task<Result<T>> QueryAsync<T>(IQuery<T> query, QueryContext ctx, CancellationToken ct = default) =>
            Task.FromResult(Result<T>.Fail(ErrorCodes.StoreUnavailable));
    }

    private static SalesQueryService Service(out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher>(new UnavailableDispatcher());
        services.AddSingleton<ICurrentUserProvider, QueryOnlyUserProvider>();
        services.AddSingleton<IClock>(new TestClock(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<IClinicTimeZoneProvider, TestZone>();
        provider = services.BuildServiceProvider();
        return new SalesQueryService(provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task FailedReads_Throw_InsteadOfReturningAnEmptyResult()
    {
        var service = Service(out var provider);
        using var _ = provider;

        await Assert.ThrowsAsync<ServiceException>(() => service.GetReadyEncountersAsync());
        await Assert.ThrowsAsync<ServiceException>(() => service.GetInvoicesAsync());
        await Assert.ThrowsAsync<ServiceException>(() => service.GetInvoicesForOwnerAsync("OWN-1"));
        await Assert.ThrowsAsync<ServiceException>(() => service.GetInsurersAsync());
        await Assert.ThrowsAsync<ServiceException>(() => service.GetOwnersAsync("سارا"));
        await Assert.ThrowsAsync<ServiceException>(() => service.GetCatalogAsync());
        // A failure is not "the id is unknown": only a SUCCESSFUL query with no row answers null.
        await Assert.ThrowsAsync<ServiceException>(() => service.GetInvoiceAsync("INV-1"));
    }

    [Fact]
    public async Task FailedMutation_Throws_InsteadOfReturningTheUnchangedInvoice()
    {
        var service = Service(out var provider);
        using var _ = provider;

        await Assert.ThrowsAsync<ServiceException>(() => service.SetInsuranceAsync("INV-1", "INS-1"));
        await Assert.ThrowsAsync<ServiceException>(() => service.SetInsuranceAsync("INV-1", null));
    }

    [Fact]
    public async Task NoSession_Throws_InsteadOfLookingLikeAnEmptyLedger()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher>(new UnavailableDispatcher());
        services.AddSingleton<ICurrentUserProvider>(new AnonymousOnlyUserProvider());
        services.AddSingleton<IClock>(new TestClock(DateTimeOffset.UnixEpoch));
        services.AddSingleton<IClinicTimeZoneProvider, TestZone>();
        using var provider = services.BuildServiceProvider();
        var service = new SalesQueryService(provider.GetRequiredService<IServiceScopeFactory>());

        var error = await Assert.ThrowsAsync<ServiceException>(() => service.GetInvoicesAsync());
        Assert.Equal(ErrorCodes.SessionExpired.Code, error.Message);
    }

    /// <summary>An unauthenticated provider: the query context cannot be built at all.</summary>
    private sealed class AnonymousOnlyUserProvider : ICurrentUserProvider
    {
        public Result<QueryContext> BuildQueryContext() => Result<QueryContext>.Fail(ErrorCodes.SessionExpired);
        public QueryContext BuildAnonymousQueryContext() => new("T-1", ICurrentUserProvider.AnonymousUserId, "COR-1");
        public Result<CommandContext> BuildAnonymousCommandContext(string? operationId = null) => throw new NotSupportedException();
        public Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
            long? expectedVersion, CancellationToken ct) => throw new NotSupportedException();
    }
}
