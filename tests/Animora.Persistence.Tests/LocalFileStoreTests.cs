using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Storage;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A11c's content-addressed local file store: a put/open/delete round trip, deduplication of
/// identical content, and rejection of a malformed (path-traversing) hash.
/// </summary>
public sealed class LocalFileStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly LocalFileStore _store;

    public LocalFileStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "animora-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var options = new LocalStoreOptions { DatabasePath = Path.Combine(_directory, "animora.db") };
        _store = new LocalFileStore(Options.Create(options));
    }

    [Fact]
    public async Task PutOpenDelete_RoundTrip()
    {
        var content = Encoding.UTF8.GetBytes("boarding receipt scan");

        var put = await _store.PutAsync(new MemoryStream(content), default);
        Assert.True(put.IsSuccess, put.Detail);
        var hash = put.Value!;
        Assert.Matches("^[0-9a-f]{64}$", hash);

        var open = await _store.OpenAsync(hash, default);
        Assert.True(open.IsSuccess);
        await using (var stream = open.Value!)
        {
            using var reader = new MemoryStream();
            await stream.CopyToAsync(reader);
            Assert.Equal(content, reader.ToArray());
        }

        Assert.True((await _store.DeleteAsync(hash, default)).IsSuccess);
        var missing = await _store.OpenAsync(hash, default);
        Assert.True(missing.IsFailure);
        Assert.Equal(ErrorCodes.NotFound, missing.Error);
    }

    [Fact]
    public async Task IdenticalContent_IsStoredOnce()
    {
        var content = Encoding.UTF8.GetBytes("same bytes");
        var first = await _store.PutAsync(new MemoryStream(content), default);
        var second = await _store.PutAsync(new MemoryStream(content), default);

        Assert.Equal(first.Value, second.Value);

        var files = Directory.GetFiles(Path.Combine(_directory, "files"), "*", SearchOption.AllDirectories);
        Assert.Single(files); // deduplicated.
    }

    [Fact]
    public async Task MalformedHash_IsRejected()
    {
        var open = await _store.OpenAsync("../../etc/passwd", default);
        Assert.True(open.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, open.Error);

        var delete = await _store.DeleteAsync("NOT-A-HASH", default);
        Assert.True(delete.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, delete.Error);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
