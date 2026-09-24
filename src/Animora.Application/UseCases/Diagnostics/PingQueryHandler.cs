using Animora.Application.Abstractions;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Diagnostics;

/// <summary>Answers <see cref="PingQuery"/> with <see cref="Pong"/>.</summary>
public sealed class PingQueryHandler : IQueryHandler<PingQuery, string>
{
    public const string Pong = "PONG";

    public Task<Result<string>> HandleAsync(PingQuery query, QueryContext context, CancellationToken ct) =>
        Task.FromResult(Result<string>.Ok(Pong));
}
