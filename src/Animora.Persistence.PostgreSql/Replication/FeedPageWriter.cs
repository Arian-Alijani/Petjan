using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Contracts.Wire.Sync;
using Microsoft.Extensions.Options;
namespace Animora.Persistence.PostgreSql.Replication;

/// <summary>Bounded two-pass page materialization after snapshot commit. Files precede Ready metadata.</summary>
public sealed class FeedPageWriter(IOptions<FeedStorageOptions> options)
{
    public string FeedRoot => Path.Combine(options.Value.DataRootPath, "feed");
    public string CreateWorkspace()
    {
        if (!Path.IsPathRooted(options.Value.DataRootPath)) throw new InvalidOperationException("An absolute feed data root is required.");
        var path = Path.Combine(FeedRoot, "_staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    public string BatchDirectory(string tenant, string batch)
    {
        ValidateSegment(tenant); ValidateSegment(batch);
        return Path.Combine(FeedRoot, tenant, batch);
    }
    public static void ValidateSegment(string value)
    {
        if (value.Length != 26 || !value.All(c => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(c)))
            throw new ArgumentException("Invalid feed identifier.");
    }
    public async Task<FeedPageWriteResult> WriteAsync(string tenant, string batch, string workspace,
        IAsyncEnumerable<SyncRecord> records, CancellationToken ct)
    {
        // Raw per-page arrays are staged first to learn PageCount without holding a batch in memory.
        int count = 0; int bytes = 2; var page = new List<SyncRecord>();
        async Task Stage()
        {
            await File.WriteAllBytesAsync(Path.Combine(workspace, $"{count++}.json"),
                JsonSerializer.SerializeToUtf8Bytes(page, SyncSerializerV2.Options), ct).ConfigureAwait(false);
            page.Clear(); bytes = 2;
        }
        await foreach (var record in records.WithCancellation(ct).ConfigureAwait(false))
        {
            int size = JsonSerializer.SerializeToUtf8Bytes(record, SyncSerializerV2.Options).Length + 1;
            if (size + 1024 > FeedStorageOptions.PageTargetBytes)
                throw new InvalidOperationException("A root snapshot exceeds the sync page limit.");
            if (bytes + size + 1024 > FeedStorageOptions.PageTargetBytes && page.Count != 0) await Stage().ConfigureAwait(false);
            page.Add(record); bytes += size;
        }
        if (page.Count != 0 || count == 0) await Stage().ConfigureAwait(false);
        var dir = BatchDirectory(tenant, batch); Directory.CreateDirectory(dir);
        var metadataPath = Path.Combine(workspace, "pages.ndjson");
        await using var metadata = new StreamWriter(metadataPath, false, new UTF8Encoding(false));
        using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long totalBytes = 0;
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            var raw = await File.ReadAllBytesAsync(Path.Combine(workspace, $"{ordinal}.json"), ct).ConfigureAwait(false);
            var values = JsonSerializer.Deserialize<SyncRecord[]>(raw, SyncSerializerV2.Options)!;
            // PageHash cannot hash itself; the authoritative compressed-byte hash is metadata/ETag.
            var json = JsonSerializer.SerializeToUtf8Bytes(new SyncPage(batch, ordinal, count, values, ""), SyncSerializerV2.Options);
            if (json.Length > FeedStorageOptions.PageTargetBytes) throw new InvalidOperationException("Sync page exceeded its byte budget.");
            var temporary = Path.Combine(dir, $"{ordinal}.{Guid.NewGuid():N}.tmp");
            var final = Path.Combine(dir, $"{ordinal}.gz");
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                        await gzip.WriteAsync(json, ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                var hash = await HashFile(temporary, ct).ConfigureAwait(false);
                try { File.Move(temporary, final); }
                catch (IOException) when (File.Exists(final))
                {
                    if (!StringComparer.Ordinal.Equals(hash, await HashFile(final, ct).ConfigureAwait(false)))
                        throw new InvalidOperationException("Immutable batch content mismatch.");
                    File.Delete(temporary);
                }
                int compressedSize = checked((int)new FileInfo(final).Length);
                totalBytes += compressedSize;
                contentHash.AppendData(Encoding.ASCII.GetBytes(hash));
                await metadata.WriteLineAsync(JsonSerializer.Serialize(new FeedBatchPageRow
                    { TenantId = tenant, BatchId = batch, PageOrdinal = ordinal, PageHash = hash, SizeBytes = compressedSize },
                    SyncSerializerV2.Options).AsMemory(), ct).ConfigureAwait(false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        await metadata.FlushAsync(ct).ConfigureAwait(false);
        return new(count, totalBytes, Convert.ToHexStringLower(contentHash.GetHashAndReset()), metadataPath);
    }
    private static async Task<string> HashFile(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false));
    }
    internal static async IAsyncEnumerable<SyncRecord> ReadSpool(string path, [EnumeratorCancellation] CancellationToken ct)
    {
        using var input = new StreamReader(path, Encoding.UTF8);
        while (await input.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            yield return JsonSerializer.Deserialize<SyncRecord>(line, SyncSerializerV2.Options)!;
    }
}

/// <summary>Fixed-size result; page metadata is streamed from the workspace rather than retained per batch.</summary>
public sealed record FeedPageWriteResult(int Count, long SizeBytes, string ContentHash, string MetadataPath)
{
    public async IAsyncEnumerable<FeedBatchPageRow> ReadMetadata([EnumeratorCancellation] CancellationToken ct = default)
    {
        using var input = new StreamReader(MetadataPath, Encoding.UTF8);
        while (await input.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            yield return JsonSerializer.Deserialize<FeedBatchPageRow>(line, SyncSerializerV2.Options)!;
    }
}
