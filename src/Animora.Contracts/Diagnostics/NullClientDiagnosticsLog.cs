namespace Animora.Contracts.Diagnostics;

/// <summary>Silent default for hosts without a client diagnostic surface.</summary>
public sealed class NullClientDiagnosticsLog : IClientDiagnosticsLog
{
    public bool IsDebugEnabled => false;
    public void Info(string category, string message) { }
    public void Warn(string category, string message) { }
    public void Debug(string category, string message) { }
}
