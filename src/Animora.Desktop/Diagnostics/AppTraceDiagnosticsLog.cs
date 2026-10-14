using Animora.Contracts.Diagnostics;

namespace Animora.Desktop.Diagnostics;

/// <summary>Bridges lower-layer diagnostics to the live launcher console and dated AppTrace file.</summary>
public sealed class AppTraceDiagnosticsLog : IClientDiagnosticsLog
{
    public bool IsDebugEnabled => AppTrace.IsDebugEnabled;
    public void Info(string category, string message)
    {
        try { AppTrace.Info(message, category); } catch { /* Diagnostics must not affect the operation. */ }
    }
    public void Warn(string category, string message)
    {
        try { AppTrace.Warn(message, category); } catch { /* Diagnostics must not affect the operation. */ }
    }
    public void Debug(string category, string message)
    {
        try { AppTrace.Debug(message, category); } catch { /* Diagnostics must not affect the operation. */ }
    }
}
