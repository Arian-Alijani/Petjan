using System.Text.Json;
using Animora.Contracts.Wire.Commands;
using Animora.Contracts.Wire.Errors;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Server.Hosting;

namespace Animora.Server.Commands;

internal static class CommandEndpoints
{
    public const int MaxBatchSize = 50;

    public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ServerRoutes.Commands)
            .RequireRateLimiting(RateLimitPolicies.Commands).RequireAuthorization();
        group.MapPost("", SubmitAsync);
        group.MapPost("/batch", BatchAsync);
        group.MapGet("/{operationId}/ids", GetIdsAsync);
        return endpoints;
    }

    internal static async Task<IResult> SubmitAsync(HttpContext context, CommandIntakeService intake, CancellationToken ct = default)
    {
        try
        {
            CommandEnvelope? envelope = await context.Request.ReadFromJsonAsync<CommandEnvelope>(ct).ConfigureAwait(false);
            Result valid = CommandEnvelopeValidator.Validate(envelope);
            if (valid.IsFailure) return TransportError(context, valid);
            return ToHttp(context, await intake.SubmitAsync(envelope!, ct).ConfigureAwait(false));
        }
        catch (JsonException) { return TransportError(context, Result.Fail(ErrorCodes.ValidationFailed, "malformed JSON")); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status400BadRequest)
        { return TransportError(context, Result.Fail(ErrorCodes.ValidationFailed, "malformed JSON")); }
        catch (InvalidOperationException) when (!context.Request.HasJsonContentType())
        { return TransportError(context, Result.Fail(ErrorCodes.ValidationFailed, "JSON required")); }
    }

    internal static async Task<IResult> BatchAsync(HttpContext context, CommandIntakeService intake, CancellationToken ct = default)
    {
        try
        {
            CommandEnvelope[]? envelopes = await context.Request.ReadFromJsonAsync<CommandEnvelope[]>(ct).ConfigureAwait(false);
            Result valid = ValidateBatch(envelopes);
            if (valid.IsFailure) return TransportError(context, valid);
            var results = new List<SettlementResult>(envelopes!.Length);
            foreach (CommandEnvelope envelope in envelopes)
            {
                // Sequential request order, separate intake transaction for every item. Never Task.WhenAll.
                Result<SettlementResult> settled = await intake.SubmitAsync(envelope, ct).ConfigureAwait(false);
                if (settled.IsFailure) return TransportError(context, settled.ToResult());
                results.Add(settled.Value!);
            }
            return Results.Json(results);
        }
        catch (JsonException) { return TransportError(context, Result.Fail(ErrorCodes.ValidationFailed, "malformed JSON")); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status400BadRequest)
        { return TransportError(context, Result.Fail(ErrorCodes.ValidationFailed, "malformed JSON")); }
        catch (InvalidOperationException) when (!context.Request.HasJsonContentType())
        { return TransportError(context, Result.Fail(ErrorCodes.ValidationFailed, "JSON required")); }
    }

    internal static Result ValidateBatch(CommandEnvelope[]? envelopes)
    {
        if (envelopes is null) return Result.Fail(ErrorCodes.ValidationFailed, "batch required");
        if (envelopes.Length > MaxBatchSize) return Result.Fail(ErrorCodes.BatchTooLarge);
        // Validate the whole transport before processing the first item (no partial malformed batches).
        foreach (CommandEnvelope envelope in envelopes)
        {
            Result valid = CommandEnvelopeValidator.Validate(envelope);
            if (valid.IsFailure) return valid;
        }
        return Result.Ok();
    }

    private static async Task<IResult> GetIdsAsync(string operationId, HttpContext context,
        ICommandIntakeStore store, CancellationToken ct = default)
    {
        if (!CommandEnvelopeValidator.IsUlid(operationId))
            return TransportError(context, Result.Fail(ErrorCodes.ValidationFailed, "operationId"));
        IReadOnlyList<CommandIdMapItem> ids = await store.GetIdMapAsync(operationId, ct).ConfigureAwait(false);
        return Results.Json(new CommandIdMapResponse(ids.Select(i =>
            new CommandIdMapResponseItem(i.Slot, i.EntityType, i.ServerEntityId)).ToArray()));
    }

    internal static IResult ToHttp(HttpContext context, Result<SettlementResult> result) =>
        result.IsSuccess ? Results.Json(result.Value) : TransportError(context, result.ToResult());

    internal static IResult TransportError(HttpContext context, Result failure)
    {
        int status = failure.Error == ErrorCodes.ContractVersionUnsupported ? StatusCodes.Status426UpgradeRequired
            : failure.Error == ErrorCodes.BatchTooLarge ? StatusCodes.Status413PayloadTooLarge
            : failure.Error == ErrorCodes.ValidationFailed ? StatusCodes.Status400BadRequest
            : failure.Error == ErrorCodes.SessionExpired ? StatusCodes.Status401Unauthorized
            : failure.Error.Category is ErrorCode.Forbidden or ErrorCode.Entitlement ? StatusCodes.Status403Forbidden
            : StatusCodes.Status503ServiceUnavailable;
        if (status == StatusCodes.Status426UpgradeRequired)
            context.Response.Headers["X-Animora-Supported-Versions"] = "1,2";
        string correlation = context.Response.Headers[CorrelationHeader.Name].ToString();
        if (string.IsNullOrWhiteSpace(correlation)) correlation = CorrelationHeader.Resolve(context);
        return Results.Json(new WireApiError(failure.Error.Code, failure.Detail, [], correlation), statusCode: status);
    }
}
