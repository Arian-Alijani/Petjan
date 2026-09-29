using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// C10: one persisted UI/app preference for a user. Backs the Profile &amp; Settings screen's
/// <c>AppSettings</c> and the editable profile fields. Per ARCHITECTURE.md §8.5, UI preferences are
/// the one data category eligible for generic last-writer-wins replication, which is exactly why the
/// profile save commands are <b>not</b> journaled — so this row carries no business intent.
/// </summary>
[LocalOnly]
public sealed class UserPreference
{
    private UserPreference() { }

    /// <summary>ULID primary key.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Owning user.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Preference key (e.g. "settings.theme", "profile.phone").</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>JSON-encoded value (string/number/bool/array).</summary>
    public string ValueJson { get; private set; } = string.Empty;

    /// <summary>Creates a new preference row.</summary>
    public static UserPreference Create(string tenantId, string userId, string key, string valueJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new()
        {
            Id = IdGenerator.NewId(),
            TenantId = tenantId,
            UserId = userId,
            Key = key,
            ValueJson = valueJson ?? string.Empty,
        };
    }

    /// <summary>Replaces the stored value (last-writer-wins).</summary>
    public void SetValue(string valueJson) => ValueJson = valueJson ?? string.Empty;
}
