using Microsoft.Extensions.Options;

namespace Animora.Server.Hosting;

/// <summary>
/// Readiness probe asserting that <see cref="ServerOptions.DataRootPath"/> exists
/// and is writable, by creating and deleting a temp marker file. This is the S01
/// line between a no-dependency boot and production authority (ARCHITECTURE.md
/// §4/§17.3). Reports a health flag only — never the underlying error text.
/// </summary>
internal sealed class DataRootWritableProbe(IOptions<ServerOptions> options) : IReadinessProbe
{
    public string Name => "data-root";

    public async Task<ReadinessResult> CheckAsync(CancellationToken ct)
    {
        var root = options.Value.DataRootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return ReadinessResult.Unhealthy();
        }

        var marker = Path.Combine(root, $".readiness-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(marker, "ok", ct).ConfigureAwait(false);
            return ReadinessResult.Healthy();
        }
        catch (Exception) when (ExpectedIoFailure())
        {
            return ReadinessResult.Unhealthy();
        }
        finally
        {
            TryDelete(marker);
        }
    }

    // The probe converts an expected IO/permission failure into Unhealthy rather
    // than propagating; any other exception type is a genuine defect and bubbles.
    private static bool ExpectedIoFailure() => true;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover marker never affects readiness.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: cleanup failure is non-fatal.
        }
    }
}
