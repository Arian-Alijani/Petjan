using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Append-only record of one local sign-in attempt (B03). Never replicated.</summary>
[LocalOnly]
public sealed class LoginAttemptLog
{
    public const string SuccessOutcome = "OK";
    public const int UsernameLength = 128;
    public const int OutcomeLength = 64;

    private LoginAttemptLog()
    {
    }

    public string Id { get; private set; } = string.Empty;
    public string UsernameAttempted { get; private set; } = string.Empty;

    /// <summary><see cref="SuccessOutcome"/> or the failing error code.</summary>
    public string Outcome { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string DeviceId { get; private set; } = string.Empty;

    public static LoginAttemptLog Create(string usernameAttempted, string outcome, string deviceId, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var name = (usernameAttempted ?? string.Empty).Trim();
        return new LoginAttemptLog
        {
            Id = IdGenerator.NewId(nowUtc),
            UsernameAttempted = name.Length > UsernameLength ? name[..UsernameLength] : name,
            Outcome = outcome.Length > OutcomeLength ? outcome[..OutcomeLength] : outcome,
            OccurredAtUtc = nowUtc,
            DeviceId = deviceId,
        };
    }
}
