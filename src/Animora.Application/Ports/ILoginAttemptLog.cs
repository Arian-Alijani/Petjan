namespace Animora.Application.Ports;

/// <summary>Append-only local sign-in audit (B03). Rows are flushed by the next <c>IUnitOfWork.SaveAsync</c>.</summary>
public interface ILoginAttemptLog
{
    public const string SuccessOutcome = "OK";

    void Append(string usernameAttempted, string outcome, string deviceId, DateTimeOffset nowUtc);
}
