namespace Animora.Application.Commands;

/// <summary>
/// Sink for integrity events raised by the command pipeline (A-RULE 019). A fingerprint mismatch on a replayed
/// <c>OperationId</c> is a security event that must be audited, never silently treated as a replay. The real
/// implementation lands in B05; the default is a no-op so the pipeline is complete today.
/// </summary>
public interface IIntegrityAlertSink
{
    /// <summary>Records an integrity alert. Best-effort: a sink failure never masks the original result.</summary>
    void Raise(string code, string detail);
}

/// <summary>Default <see cref="IIntegrityAlertSink"/>: swallows the alert (no sink wired yet — B05 replaces it).</summary>
public sealed class NullIntegrityAlertSink : IIntegrityAlertSink
{
    public void Raise(string code, string detail)
    {
        // No-op by design: B05 registers the real audit-backed sink.
    }
}
