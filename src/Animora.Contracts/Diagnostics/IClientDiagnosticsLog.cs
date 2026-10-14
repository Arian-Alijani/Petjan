namespace Animora.Contracts.Diagnostics;

/// <summary>Best-effort client diagnostics. Implementations must never throw and must not log
/// credentials, personal record fields or free-text search terms. Category precedes message.</summary>
public interface IClientDiagnosticsLog
{
    bool IsDebugEnabled { get; }
    void Info(string category, string message);
    void Warn(string category, string message);
    void Debug(string category, string message);
}
