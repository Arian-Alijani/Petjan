using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Animora.Application.Ports;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Storage;

/// <summary>
/// Layout: <c>&lt;data&gt;/files/&lt;sha256[0..2]&gt;/&lt;sha256&gt;</c>. Writes stream to a temp file while hashing,
/// then move atomically; identical content is stored once. Hashes are validated (no path traversal).
/// </summary>
public sealed partial class LocalFileStore(IOptions<LocalStoreOptions> options) : ILocalFileStore
{
    private const int BufferSize = 81920;

    private string Root => Path.Combine(options.Value.DataDirectory, "files");

    public async Task<Result<string>> PutAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var temp = Path.Combine(Root, ".tmp", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            string hash;
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BufferSize, FileOptions.Asynchronous))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                await file.FlushAsync(ct);
                hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            }

            var target = PathFor(hash);
            if (File.Exists(target)) return Result<string>.Ok(hash);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                File.Move(temp, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
                // Concurrent writer stored the same content first: dedupe.
            }
            return Result<string>.Ok(hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail(ErrorCodes.StoreUnavailable, ex.Message);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public Task<Result<Stream>> OpenAsync(string hash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsValid(hash)) return Task.FromResult(Result<Stream>.Fail(ErrorCodes.ValidationFailed, "Invalid file hash."));
        var path = PathFor(hash);
        if (!File.Exists(path)) return Task.FromResult(Result<Stream>.Fail(ErrorCodes.NotFound, hash));
        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                FileOptions.Asynchronous);
            return Task.FromResult(Result<Stream>.Ok(stream));
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult(Result<Stream>.Fail(ErrorCodes.NotFound, hash));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Result<Stream>.Fail(ErrorCodes.StoreUnavailable, ex.Message));
        }
    }

    public Task<Result> DeleteAsync(string hash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsValid(hash)) return Task.FromResult(Result.Fail(ErrorCodes.ValidationFailed, "Invalid file hash."));
        var path = PathFor(hash);
        if (!File.Exists(path)) return Task.FromResult(Result.Fail(ErrorCodes.NotFound, hash));
        try
        {
            File.Delete(path);
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Result.Fail(ErrorCodes.StoreUnavailable, ex.Message));
        }
    }

    private string PathFor(string hash) => Path.Combine(Root, hash[..2], hash);

    private static bool IsValid(string? hash) => hash is not null && HashPattern().IsMatch(hash);

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
