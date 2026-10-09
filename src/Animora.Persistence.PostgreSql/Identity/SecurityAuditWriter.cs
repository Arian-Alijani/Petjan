using System.Text.Json;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>Append-only sink for security-audit rows.</summary>
public interface ISecurityAuditWriter
{
    /// <summary>
    /// Stages <paramref name="entry"/> and commits it <b>together with every state change already staged
    /// on the same unit of work</b> in one transaction, so a security action and its audit row are atomic.
    /// A concurrent-update / duplicate-key fault is returned as <c>ConcurrencyConflict</c>, never thrown.
    /// </summary>
    Task<Result> WriteAsync(SecurityAuditEntry entry, CancellationToken ct = default);
}

/// <inheritdoc cref="ISecurityAuditWriter" />
public sealed class SecurityAuditWriter(AnimoraServerDbContext context) : ISecurityAuditWriter
{
    /// <inheritdoc />
    public async Task<Result> WriteAsync(SecurityAuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.SecurityAudit.Add(entry);
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict, "unique");
        }
    }
}

/// <summary>
/// Builds <see cref="SecurityAuditEntry.DetailJson"/> and refuses any key that could carry a secret
/// (password, token, hash, secret, key, stamp, credential), so credentials can never reach the audit trail.
/// </summary>
public static class SecurityAuditDetail
{
    private static readonly string[] ForbiddenKeyFragments = ["password", "token", "hash", "secret", "key", "stamp", "credential"];

    /// <summary>Serializes non-secret key/value detail; returns <see langword="null"/> when empty.</summary>
    /// <exception cref="ArgumentException">A key looks like it would carry a secret.</exception>
    public static string? Create(params (string Key, string? Value)[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Length == 0)
        {
            return null;
        }

        var map = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach ((string key, string? value) in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            foreach (string fragment in ForbiddenKeyFragments)
            {
                if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Audit detail key '{key}' may carry a secret and is not allowed.", nameof(entries));
                }
            }

            map[key] = value;
        }

        return JsonSerializer.Serialize(map);
    }
}
