using Serilog.Events;

namespace Animora.Infrastructure.Local.Options;

/// <summary>Options for the local, buffered and rolling file log.</summary>
public sealed class LocalLoggingOptions
{
    public string DataDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Animora");
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;
    public int RetainedFiles { get; set; } = 14;
    public long FileSizeLimitBytes { get; set; } = 25L * 1024 * 1024;
    public bool EnableSqlLogging { get; set; }
}
