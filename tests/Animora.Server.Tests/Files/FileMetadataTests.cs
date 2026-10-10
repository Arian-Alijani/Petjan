using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Files;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Files;
using Animora.Server.Hosting;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
namespace Animora.Server.Tests.Files;

public sealed class FileMetadataTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private sealed record Tenant(string TenantId) : ITenantContext { public bool IsOperator => false; }
    private AnimoraServerDbContext Db(string tenant) => new(new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(pg.ConnectionString).Options,new Tenant(tenant));
    [PgFact] public async Task CrossTenant_CannotDownload_ByGuessingId()
    {
        await using var a = Db(FileStoreTests.Tenant); await a.Database.MigrateAsync();
        var store = new FileStore(Options.Create(new ServerOptions { DataRootPath = Path.GetTempPath() }));
        var result = await new FileTransferService(a,store,TimeProvider.System).DeclareAsync("user",new("note.txt","text/plain",5,null),default);
        Assert.True(result.IsSuccess);
        await using var b = Db("01K00000000000000000000003");
        Assert.Null(await new FileTransferService(b,store,TimeProvider.System).FindAsync(result.Value!.Id,false,default));
        Assert.False(await new FileExistenceChecker(b).IsAvailableAsync(FileStoreTests.Tenant,result.Value.Id));
    }
    [PgFact] public async Task CommitAndRefCount_SameTransaction_AndResendAndHashMismatch()
    {
        string root = Path.Combine(Path.GetTempPath(),"animora-pg-files-"+Guid.NewGuid().ToString("N"));
        try
        {
            await using var db = Db(FileStoreTests.Tenant); await db.Database.MigrateAsync();
            var store = new FileStore(Options.Create(new ServerOptions { DataRootPath = root })); var service = new FileTransferService(db,store,TimeProvider.System);
            var row = (await service.DeclareAsync("user",new("note.txt","text/plain",5,null),default)).Value!;
            var wrong = await service.WriteAsync(row.UploadId!,2,"hello"u8.ToArray(),default); Assert.False(wrong.Accepted); Assert.Equal(0,wrong.NextOffset);
            Assert.True((await service.WriteAsync(row.UploadId!,0,"hello"u8.ToArray(),default)).Accepted);
            Assert.True((await service.WriteAsync(row.UploadId!,0,"hello"u8.ToArray(),default)).Accepted);
            Assert.True((await service.CompleteAsync(row.UploadId!,default)).IsSuccess);
            Assert.Equal(FileState.Available,row.State); Assert.Equal(1,(await db.BlobRefs.SingleAsync(x=>x.Sha256==row.Sha256)).RefCount);
            await using var other = Db("01K00000000000000000000003"); var otherService = new FileTransferService(other,store,TimeProvider.System);
            var second = (await otherService.DeclareAsync("user",new("second.txt","text/plain",5,null),default)).Value!;
            await otherService.WriteAsync(second.UploadId!,0,"hello"u8.ToArray(),default); await otherService.CompleteAsync(second.UploadId!,default);
            db.ChangeTracker.Clear(); Assert.Equal(2,(await db.BlobRefs.SingleAsync(x=>x.Sha256==row.Sha256)).RefCount);
            var bad = (await service.DeclareAsync("user",new("bad.txt","text/plain",5,new string('a',64)),default)).Value!;
            await service.WriteAsync(bad.UploadId!,0,"hello"u8.ToArray(),default);
            Assert.True((await service.CompleteAsync(bad.UploadId!,default)).IsFailure); Assert.Equal(FileState.Mismatch,bad.State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
