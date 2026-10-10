using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Commands;

namespace Animora.Server.Commands;

/// <summary>Handler result metadata; S07–S10 supply server-allocated ids, never client guesses.</summary>
internal interface ICommandEffectMetadata
{
    IReadOnlyList<CommandIdMapItem> CreatedIds { get; }
    string? ServerVersion { get; }
}

internal interface IFinancialEffectReporter
{
    FinancialEffect FinancialEffect { get; }
}

internal sealed record CommandDispatchResult(Result Result, object? Value, FinancialEffect FinancialEffect) : IFinancialEffectReporter;

internal static class SettlementMapper
{
    public static SettlementResult Map(string operationId, Result result, DateTimeOffset now,
        FinancialEffect financialEffect = FinancialEffect.None, string? entityId = null, string? version = null,
        string? ruleDetail = null)
    {
        string? detail = result.Detail;
        if (ruleDetail is not null) detail = detail is null ? ruleDetail : $"{detail}; {ruleDetail}";
        if (detail is { Length: > 512 }) detail = detail[..512];
        return new SettlementResult(operationId,
            result.IsSuccess ? SettlementOutcome.Accepted
                : result.Error == ErrorCodes.ConcurrencyConflict ? SettlementOutcome.Conflict : SettlementOutcome.Rejected,
            result.IsSuccess ? null : result.Error.Code, detail,
            result.IsSuccess ? entityId : null, result.IsSuccess ? version : null, now,
            result.IsSuccess ? financialEffect : FinancialEffect.NotApplied,
            result.IsSuccess ? null : ErrorResponsibility.For(result.Error.Code));
    }

    public static SettlementResult Map<T>(string operationId, Result<T> result, DateTimeOffset now) =>
        Map(operationId, result.ToResult(), now,
            result.Value is IFinancialEffectReporter reporter ? reporter.FinancialEffect : FinancialEffect.None,
            result.Value is string id ? id : null,
            (result.Value as ICommandEffectMetadata)?.ServerVersion);

    public static SettlementResult Stored(CommandIntakeRecord row) => new(row.OperationId,
        (SettlementOutcome)row.Outcome!.Value, row.ErrorCode, row.Detail, row.ServerEntityId,
        row.ServerVersion?.ToString("D"), row.SettledUtc!.Value, (FinancialEffect)row.FinancialEffect, row.ResponsibleRole);
}
