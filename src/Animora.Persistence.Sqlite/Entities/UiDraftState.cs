using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Device-local, explicitly opted-in in-flight UI state. Never replicated.</summary>
[LocalOnly]
public sealed class UiDraftState
{
    public const int ScreenKeyMaxLength = 100;
    public const int PayloadMaxLength = 65536;

    private UiDraftState()
    {
    }

    public string Id { get; private set; } = string.Empty;
    public string ScreenKey { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static UiDraftState Create(string screenKey, string userId, string payloadJson, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(payloadJson);
        return new UiDraftState
        {
            Id = IdGenerator.NewId(nowUtc),
            ScreenKey = screenKey,
            UserId = userId,
            PayloadJson = payloadJson,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void ReplacePayload(string payloadJson, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        PayloadJson = payloadJson;
        UpdatedAtUtc = nowUtc;
    }
}
