using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Visits;
using Animora.Contracts.Visits;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Visits;

/// <summary>
/// Lab-test type catalog seam. Every call dispatches an Application use case in its own scope, so the
/// authorization stage (<c>visits.read</c> for reads, <c>settings.write</c> for mutations) applies to a direct
/// Contracts call exactly as it does to the settings tab. Holds no state and does no file I/O.
/// </summary>
public sealed class LabTestSettingsService(IServiceScopeFactory scopes) : ILabTestSettingsService
{
    public async Task<IReadOnlyList<LabTestType>> GetTypesAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new GetLabTestTypesQuery(), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    public Task<LabTestType> AddTypeAsync(string name, string? defaultUnit) =>
        SendAsync(new AddLabTestTypeCommand(name, defaultUnit));

    public Task RemoveTypeAsync(string typeId) => SendAsync(new RemoveLabTestTypeCommand(typeId));

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
