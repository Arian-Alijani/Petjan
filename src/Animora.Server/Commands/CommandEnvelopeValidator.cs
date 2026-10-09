using System.Text.Json;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;

namespace Animora.Server.Commands;

internal static class CommandEnvelopeValidator
{
    public static Result Validate(CommandEnvelope? envelope)
    {
        if (envelope is null) return Result.Fail(ErrorCodes.ValidationFailed, "envelope required");
        if (!WireContract.IsSupported(envelope.ContractVersion)) return Result.Fail(ErrorCodes.ContractVersionUnsupported);
        if (!IsUlid(envelope.OperationId) || !IsUlid(envelope.TenantId) || !IsUlid(envelope.UserId) || !IsUlid(envelope.DeviceId)
            || (envelope.AggregateId is not null && !IsUlid(envelope.AggregateId))
            || (envelope.DependsOnOperationId is not null && !IsUlid(envelope.DependsOnOperationId))
            || (envelope.CausationId is not null && !IsUlid(envelope.CausationId))
            || string.IsNullOrWhiteSpace(envelope.CommandType) || envelope.CommandType.Length > 64
            || string.IsNullOrWhiteSpace(envelope.AggregateType) || envelope.AggregateType.Length > 32
            || envelope.CommandFingerprint is not { Length: 64 }
            || !envelope.CommandFingerprint.All(c => char.IsAsciiHexDigit(c))
            || string.IsNullOrWhiteSpace(envelope.PayloadJson)
            || (envelope.ExpectedServerVersion is not null && (!Guid.TryParseExact(envelope.ExpectedServerVersion, "D", out var version)
                || version.ToString("D") != envelope.ExpectedServerVersion)))
            return Result.Fail(ErrorCodes.ValidationFailed, "malformed envelope");
        try
        {
            using JsonDocument json = JsonDocument.Parse(envelope.PayloadJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return Result.Fail(ErrorCodes.ValidationFailed, "payload shape");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in json.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) return Result.Fail(ErrorCodes.ValidationFailed, "payload shape");
        }
        catch (JsonException) { return Result.Fail(ErrorCodes.ValidationFailed, "payload shape"); }
        return Result.Ok();
    }

    public static bool IsUlid(string? value) => value is { Length: 26 } && value[0] <= '7'
        && value.All(c => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(c, StringComparison.Ordinal));
}
