using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Animora.Server.Hosting;
using Microsoft.Extensions.Options;
namespace Animora.Server.Files;

internal sealed class FileStore(IOptions<ServerOptions> options)
{
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const int MaxChunkBytes = 1024 * 1024;
    public string Root => Path.Combine(options.Value.DataRootPath, "files");
    private static string Id(string value) => Regex.IsMatch(value, "^[0-9A-HJKMNP-TV-Z]{26}$", RegexOptions.CultureInvariant)
        ? value : throw new ArgumentException("Invalid file identifier.");
    public static bool IsHash(string? value) => value is not null && Regex.IsMatch(value, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
    public string Staging(string tenantId, string uploadId) => Path.Combine(Root, "staging", Id(tenantId), Id(uploadId) + ".part");
    public string Blob(string hash) => IsHash(hash) ? Path.Combine(Root, "cas", hash[..2], hash[2..4], hash) : throw new ArgumentException("Invalid content digest.");
    public async Task WriteChunkAsync(string tenantId, string uploadId, long offset, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        string path = Staging(tenantId, uploadId); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous);
        if (stream.Length < offset) throw new IOException("Staged content is incomplete.");
        // A failed metadata commit may leave trailing bytes: truncate to the last committed offset.
        stream.SetLength(offset); stream.Position = offset;
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false); stream.Flush(flushToDisk: true);
    }
    public async Task<bool> ChunkEqualsAsync(string tenantId, string uploadId, long offset, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var path = Staging(tenantId, uploadId); if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length < offset + bytes.Length) return false;
        stream.Position = offset; byte[] previous = new byte[bytes.Length];
        await stream.ReadExactlyAsync(previous, ct).ConfigureAwait(false); return previous.AsSpan().SequenceEqual(bytes.Span);
    }
    public async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }
    public async Task PublishAsync(string tenantId, string uploadId, string hash, CancellationToken ct)
    {
        string target = Blob(hash);
        if (File.Exists(target))
        {
            if(await HashAsync(target,ct).ConfigureAwait(false)!=hash)throw new IOException("CAS integrity violation.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = File.OpenRead(Staging(tenantId, uploadId)))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            { await source.CopyToAsync(output, ct).ConfigureAwait(false); await output.FlushAsync(ct).ConfigureAwait(false); output.Flush(true); }
            try { File.Move(temp, target, overwrite: false); }
            catch (IOException) when (File.Exists(target)) { /* another publisher won: immutable identical content */ }
        }
        finally { File.Delete(temp); }
    }
}
