using System.Text.RegularExpressions;
using Animora.Domain.Primitives;
using Microsoft.AspNetCore.Http;

namespace Animora.Server.Hosting;

/// <summary>
/// Resolves the request correlation id: reuse a valid inbound
/// <c>X-Correlation-Id</c> (≤64 chars, <c>[A-Za-z0-9-_]</c>), otherwise mint a
/// new ULID via <see cref="IdGenerator"/>. The resolved id is echoed on the
/// response as <c>X-Correlation-Id</c> (ARCHITECTURE.md §4).
/// </summary>
internal static partial class CorrelationHeader
{
    /// <summary>The correlation header name, in and out.</summary>
    public const string Name = "X-Correlation-Id";

    private const int MaxLength = 64;

    private static readonly object ItemKey = new();

    [GeneratedRegex(@"^[A-Za-z0-9\-_]+$")]
    private static partial Regex AllowedRegex();

    /// <summary>
    /// Resolves the correlation id for the current request. The first call decides the value and memoizes it in
    /// <see cref="HttpContext.Items"/>, so every later <c>Resolve</c> in the same request returns the SAME id even
    /// when no valid inbound header was supplied (a fresh ULID is minted only once). "Same value everywhere" is
    /// therefore structural, not a convention of who echoes first.
    /// </summary>
    public static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(ItemKey, out object? memo) && memo is string memoized)
        {
            return memoized;
        }

        string resolved = ResolveCore(context);
        context.Items[ItemKey] = resolved;
        return resolved;
    }

    private static string ResolveCore(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(Name, out var inbound))
        {
            var candidate = inbound.ToString();
            if (!string.IsNullOrEmpty(candidate) &&
                candidate.Length <= MaxLength &&
                AllowedRegex().IsMatch(candidate))
            {
                return candidate;
            }
        }

        return IdGenerator.NewId();
    }

    /// <summary>Echoes <paramref name="correlationId"/> on the response once.</summary>
    public static void Echo(HttpContext context, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[ItemKey] = correlationId;
        context.Response.Headers[Name] = correlationId;
    }
}
