using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Cashier;
using Animora.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Payments;

/// <summary>
/// Single-operator desktop flow: a cash payment needs an open drawer session for the current user, but no
/// screen exposes "open session". Before a Cash payment is dispatched, this opens a zero-float session for
/// the current tenant + user when none is open. Best effort by design — if the user may not open sessions
/// (or anything else fails) the payment command itself answers <c>CASH_SESSION_REQUIRED</c>, which the UI
/// shows in Persian, so a failure here is never swallowed silently from the operator's point of view.
/// </summary>
internal static class CashSessionAutoOpen
{
    public static async Task EnsureOpenAsync(IServiceScopeFactory scopes)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var queryContext = provider.BuildQueryContext();
        if (queryContext.IsFailure) return;
        var open = await dispatcher.QueryAsync(new OpenCashSessionQuery(), queryContext.Value!);
        if (open.IsFailure || open.Value is not null) return;

        var commandContext = await provider.BuildCommandContextAsync(null, null, null, default);
        if (commandContext.IsFailure) return;
        _ = await dispatcher.SendAsync(new OpenCashSessionCommand(Money.ZeroIrt), commandContext.Value!);
    }
}
