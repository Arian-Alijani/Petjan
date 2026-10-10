using Animora.Persistence.PostgreSql.Files;
namespace Animora.Server.Files;

internal interface IFileStoreManifest { Task WriteAsync(CancellationToken ct = default); }
internal sealed class FileStoreManifest(FileStore store, FileMaintenanceStore metadata) : IFileStoreManifest
{
    public async Task WriteAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(store.Root); string target = Path.Combine(store.Root, "manifest.sha256");
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using var writer = new StreamWriter(output, leaveOpen: true);
                await metadata.WriteManifestAsync(line => writer.WriteLineAsync(line.AsMemory(), ct), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false); output.Flush(true);
            }
            File.Move(temp, target, true);
        }
        finally { File.Delete(temp); }
    }
}
