using Animora.Server.Hosting;
namespace Animora.Server.Files;

internal sealed class FilesRootProbe(FileStore store) : IReadinessProbe
{
    public string Name => "files-root";
    public bool IsRequired => false;
    public async Task<ReadinessResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(store.Root);
            string path = Path.Combine(store.Root, ".probe-" + Guid.NewGuid().ToString("N"));
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                await output.WriteAsync(new byte[] { 0 }, ct).ConfigureAwait(false);
            var drive = DriveInfo.GetDrives().Where(d => store.Root.StartsWith(d.Name, StringComparison.Ordinal))
                .OrderByDescending(d => d.Name.Length).First();
            return new(drive.AvailableFreeSpace >= 256L * 1024 * 1024);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return ReadinessResult.Unhealthy(); }
    }
}
