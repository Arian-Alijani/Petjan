using System.Globalization;
using Animora.Domain.Results;

namespace Animora.Server.Commands;

internal sealed class RuleSetPolicy
{
    public int Current { get; init; } = 1;
    public int SupportedFrom { get; init; } = 1;

    public Result<int> Evaluate(string? version) => version is null
        ? Result<int>.Ok(Current)
        : int.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
          && parsed >= SupportedFrom && parsed <= Current
            ? Result<int>.Ok(parsed)
            : Result<int>.Fail(ErrorCodes.RuleSetUnsupported);
}
