using System.Collections.Frozen;

namespace Animora.Server.Authorization;

/// <summary>
/// Frozen map <c>wire CommandType → required PermissionCodes.*</c>. It <b>starts empty</b> in S05; the command
/// phases (S07–S10) append one row per command type. A command type with no row is <b>denied</b>
/// (<see cref="CommandAuthorizer"/> fails closed), so an unmapped command can never slip through.
/// </summary>
internal static class CommandPermissionTable
{
    /// <summary>The mapping. Append rows here (keep sorted by command type).</summary>
    public static readonly FrozenDictionary<string, string> Rows =
        new Dictionary<string, string>(StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The permission required for <paramref name="commandType"/>, or <see langword="null"/> when unmapped.</summary>
    public static string? RequiredFor(string commandType) =>
        commandType is not null && Rows.TryGetValue(commandType, out string? code) ? code : null;
}
