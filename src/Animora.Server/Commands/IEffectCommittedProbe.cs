using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Commands;

namespace Animora.Server.Commands;

/// <summary>Optional command-specific evidence for recovery; never fabricates a receipt.</summary>
internal interface IEffectCommittedProbe
{
    bool Handles(string commandType);
    Task<Result<bool>> IsCommittedAsync(CommandIntakeRecord row, CancellationToken ct = default);
}

internal sealed class NoProbe : IEffectCommittedProbe
{
    public bool Handles(string commandType) => true;
    public Task<Result<bool>> IsCommittedAsync(CommandIntakeRecord row, CancellationToken ct = default) =>
        Task.FromResult(Result<bool>.Ok(false));
}
