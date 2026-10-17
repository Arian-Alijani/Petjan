using System.Security.Claims;
using Animora.Domain.Results;
using Animora.Server.Observability;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.AspNetCore.Components.Authorization;
using Serilog.Events;
namespace Animora.Server.Admin;

/// <summary>
/// Reads the redacted in-memory log tail for the operator console. Like the status reader it is used from a circuit, so it
/// re-validates the cookie-authenticated operator and the absolute cookie expiry on EVERY read (circuits outlive HttpContext).
/// </summary>
internal sealed class AdminLiveLogAdapter(RingBufferLogSink sink, AuthenticationStateProvider authentication, TimeProvider time) : IAdminLiveLog
{
    public const int MaxSearchLength = 100;
    public async Task<Result<LogSnapshot>> ReadAsync(LogFilter filter, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var principal = (await authentication.GetAuthenticationStateAsync().ConfigureAwait(false)).User;
        if (!principal.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == OperatorCookie.Scheme) ||
            !principal.IsInRole("operator") || string.IsNullOrWhiteSpace(principal.FindFirstValue("sub")))
            return Result<LogSnapshot>.Fail(ErrorCodes.Forbidden);
        if (!long.TryParse(principal.FindFirstValue("op_exp"), out var expiry) || time.GetUtcNow().ToUnixTimeSeconds() >= expiry)
            return Result<LogSnapshot>.Fail(ErrorCodes.Forbidden);
        if (filter.Search is { Length: > MaxSearchLength }) return Result<LogSnapshot>.Fail(ErrorCodes.ValidationFailed);
        var (entries, last) = sink.Read(filter.AfterSequence, ParseLevel(filter.Level), filter.Search, filter.Take);
        return Result<LogSnapshot>.Ok(new(entries.Select(e => new LogLineModel(e.Sequence, e.Timestamp, e.Level.ToString(), e.Source, e.Message)).ToArray(), last));
    }

    /// <summary>Unknown/empty level means "everything the sink keeps" (Information and above in practice).</summary>
    internal static LogEventLevel ParseLevel(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "warning" or "warn" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "information" or "info" => LogEventLevel.Information,
        _ => LogEventLevel.Verbose
    };
}
