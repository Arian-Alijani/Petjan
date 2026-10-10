using Animora.Domain.Identity;
using Animora.Domain.Primitives;

namespace Animora.Persistence.PostgreSql.Operator;

public enum OperatorAccountStatus : short { Active = 0, Disabled = 1 }
public sealed class OperatorAccount
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public OperatorAccountStatus Status { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? LastLoginUtc { get; set; }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndUtc is { } end && end > now;
    public void FailLogin(DateTimeOffset now)
    {
        if (LockoutEndUtc is { } end && end <= now) { FailedAttempts = 0; LockoutEndUtc = null; }
        if (++FailedAttempts >= User.MaxFailedAttempts) LockoutEndUtc = now + User.LockoutDuration;
    }
    public void SucceedLogin(DateTimeOffset now)
    {
        FailedAttempts = 0; LockoutEndUtc = null; LastLoginUtc = now;
    }
    public void SetPassword(string hash, DateTimeOffset now)
    {
        PasswordHash = hash; SecurityStamp = IdGenerator.NewId(now); MustChangePassword = false;
        FailedAttempts = 0; LockoutEndUtc = null;
    }
}
