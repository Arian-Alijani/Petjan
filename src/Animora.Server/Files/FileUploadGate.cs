using Animora.Server.Hosting;
using Microsoft.Extensions.Options;

namespace Animora.Server.Files;

/// <summary>
/// BUG-FILE-01: bounds simultaneous chunk uploads per instance (<c>Server:Files:MaxConcurrentUploads</c>). The
/// rate limiter caps requests per window but not concurrency; each in-flight chunk holds up to one chunk buffer.
/// A caller that cannot get a slot within <c>UploadWaitSeconds</c> is shed (503 SERVER_BUSY + Retry-After).
/// Per-process state (single-instance component).
/// </summary>
internal sealed class FileUploadGate(IOptions<ServerOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(options.Value.Files.MaxConcurrentUploads, options.Value.Files.MaxConcurrentUploads);
    private readonly TimeSpan _wait = TimeSpan.FromSeconds(options.Value.Files.UploadWaitSeconds);

    /// <summary>Acquires a slot; <see langword="false"/> when none became free in time.</summary>
    public Task<bool> WaitAsync(CancellationToken ct) => _slots.WaitAsync(_wait, ct);

    /// <summary>Returns a slot.</summary>
    public void Release() => _slots.Release();

    /// <inheritdoc />
    public void Dispose() => _slots.Dispose();
}
