using Animora.Domain.Results;

namespace Animora.Server.Commands;

internal interface IAggregateVersionReader
{
    Task<Result<string?>> TryReadAsync(string aggregateType, string aggregateId, CancellationToken ct = default);
}

internal sealed class NoAggregateVersionReader : IAggregateVersionReader
{
    public Task<Result<string?>> TryReadAsync(string aggregateType, string aggregateId, CancellationToken ct = default) =>
        Task.FromResult(Result<string?>.Ok(null));
}
