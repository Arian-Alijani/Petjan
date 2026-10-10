using Animora.Persistence.PostgreSql.Files;
using Animora.Server.Files;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;
namespace Animora.Server.Tests.Files;

public sealed class FileLifecycleTests
{
    [Fact] public void AllowedAndForbiddenTransitions_Table()
    {
        (FileState, FileState)[] edges = [(FileState.Staged, FileState.Uploaded),(FileState.Uploaded,FileState.HashVerified),
            (FileState.Uploaded,FileState.Mismatch),(FileState.HashVerified,FileState.Committed),(FileState.Committed,FileState.Available),
            (FileState.Committed,FileState.Mismatch),(FileState.Available,FileState.Mismatch),(FileState.Staged,FileState.Abandoned),(FileState.Uploaded,FileState.Abandoned)];
        foreach (var from in Enum.GetValues<FileState>()) foreach (var to in Enum.GetValues<FileState>())
            Assert.Equal(edges.Contains((from,to)), FileLifecycle.EnsureAllowed(from,to).IsSuccess);
    }
}
public sealed class ValetKeyServiceTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    [Fact] public void RoundTrip_BoundToTenantUserDeviceFileOp()
    {
        var clock = new Clock(); var keys = new ValetKeyService(new EphemeralDataProtectionProvider(), clock);
        var binding = new ValetBinding("tenant", "user", "device", "file", "upload", 10); var token = keys.Issue(binding);
        Assert.True(keys.Validate(token, binding));
        foreach (var other in new[] { binding with { TenantId = "other" }, binding with { UserId = "other" }, binding with { DeviceId = "other" },
            binding with { FileId = "other" }, binding with { Operation = "download" }, binding with { MaxBytes = 11 } }) Assert.False(keys.Validate(token, other));
    }
    [Fact] public void Expired_Tampered_WrongTenant_WrongOp_Rejected()
    {
        var clock = new Clock(); var keys = new ValetKeyService(new EphemeralDataProtectionProvider(), clock);
        var binding = new ValetBinding("t","u","d","f","upload",10); var token = keys.Issue(binding);
        Assert.False(keys.Validate(token[..^8] + "invalid!", binding));
        Assert.False(keys.Validate(token,binding with { TenantId = "x" })); Assert.False(keys.Validate(token,binding with { Operation = "download" }));
        clock.Now += TimeSpan.FromMinutes(6); Assert.False(keys.Validate(token,binding));
    }
}
public sealed class FileTypePolicyTests
{
    [Theory] [InlineData("application/x-msdownload")] [InlineData("application/octet-stream")] [InlineData("image/svg+xml")]
    public void Executable_Rejected(string type) => Assert.False(FileTypePolicy.IsAllowed(type));
    [Fact] public void Executable_DisguisedAsText_Rejected() => Assert.False(FileTypePolicy.Matches("text/plain", "MZexe"u8));
}
public sealed class FileStoreTests
{
    internal const string Tenant = "01K00000000000000000000001";
    internal const string Upload = "01K00000000000000000000002";
    [Fact] public async Task TempThenAtomicMove_NoPartialVisible()
    {
        string root = Path.Combine(Path.GetTempPath(), "animora-files-" + Guid.NewGuid().ToString("N"));
        var store = new FileStore(Options.Create(new ServerOptions { DataRootPath = root }));
        try
        {
            await store.WriteChunkAsync(Tenant,Upload,0,"hello"u8.ToArray(),default);
            string hash = await store.HashAsync(store.Staging(Tenant,Upload),default);
            Assert.False(File.Exists(store.Blob(hash))); await store.PublishAsync(Tenant,Upload,hash,default);
            Assert.Equal("hello",await File.ReadAllTextAsync(store.Blob(hash)));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(store.Blob(hash))!,"*.tmp"));
            await store.PublishAsync(Tenant,Upload,hash,default);
            Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(store.Blob(hash))!));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Fact] public async Task ResendStoredChunk_IdenticalBytesOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), "animora-files-" + Guid.NewGuid().ToString("N"));
        var store = new FileStore(Options.Create(new ServerOptions { DataRootPath = root }));
        try
        {
            await store.WriteChunkAsync(Tenant,Upload,0,"hello"u8.ToArray(),default);
            Assert.True(await store.ChunkEqualsAsync(Tenant,Upload,0,"hello"u8.ToArray(),default));
            Assert.False(await store.ChunkEqualsAsync(Tenant,Upload,0,"world"u8.ToArray(),default));
            await store.WriteChunkAsync(Tenant,Upload,2,"!"u8.ToArray(),default);
            Assert.Equal("he!",await File.ReadAllTextAsync(store.Staging(Tenant,Upload)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Fact] public void Traversal_IsRejected()
    {
        var store = new FileStore(Options.Create(new ServerOptions { DataRootPath = "/data" }));
        Assert.Throws<ArgumentException>(() => store.Staging("../tenant",Upload)); Assert.Throws<ArgumentException>(() => store.Blob("../blob"));
    }
}
public sealed class DownloadTests
{
    [Fact] public void Headers_Attachment_NoSniff_NoStore()
    {
        var c = new DefaultHttpContext(); FileEndpoints.DownloadHeaders(c.Response);
        Assert.Equal("nosniff",c.Response.Headers["X-Content-Type-Options"]); Assert.Equal("private, no-store",c.Response.Headers.CacheControl);
        Assert.Contains("row.OriginalName, enableRangeProcessing: true", File.ReadAllText(Path.Combine(RepoRoot.Find(),"src/Animora.Server/Files/FileEndpoints.cs")));
    }
}
