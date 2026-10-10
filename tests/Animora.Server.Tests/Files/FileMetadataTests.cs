using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Files;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Files;
using Animora.Server.Hosting;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Server.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
            string contentHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("hello"u8));
            int priorReferences = await db.BlobRefs.Where(x => x.Sha256 == contentHash).Select(x => (int?)x.RefCount).SingleOrDefaultAsync() ?? 0;
            var row = (await service.DeclareAsync("user",new("note.txt","text/plain",5,null),default)).Value!;
            var wrong = await service.WriteAsync(row.UploadId!,2,"hello"u8.ToArray(),default); Assert.False(wrong.Accepted); Assert.Equal(0,wrong.NextOffset);
            Assert.True((await service.WriteAsync(row.UploadId!,0,"hello"u8.ToArray(),default)).Accepted);
            Assert.True((await service.WriteAsync(row.UploadId!,0,"hello"u8.ToArray(),default)).Accepted);
            Assert.True((await service.CompleteAsync(row.UploadId!,default)).IsSuccess);
            Assert.Equal(FileState.Available,row.State); Assert.Equal(priorReferences+1,(await db.BlobRefs.SingleAsync(x=>x.Sha256==row.Sha256)).RefCount);
            await using var other = Db("01K00000000000000000000003"); var otherService = new FileTransferService(other,store,TimeProvider.System);
            var second = (await otherService.DeclareAsync("user",new("second.txt","text/plain",5,null),default)).Value!;
            await otherService.WriteAsync(second.UploadId!,0,"hello"u8.ToArray(),default); await otherService.CompleteAsync(second.UploadId!,default);
            db.ChangeTracker.Clear(); Assert.Equal(priorReferences+2,(await db.BlobRefs.SingleAsync(x=>x.Sha256==row.Sha256)).RefCount);
            var bad = (await service.DeclareAsync("user",new("bad.txt","text/plain",5,new string('a',64)),default)).Value!;
            await service.WriteAsync(bad.UploadId!,0,"hello"u8.ToArray(),default);
            Assert.True((await service.CompleteAsync(bad.UploadId!,default)).IsFailure); Assert.Equal(FileState.Mismatch,bad.State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    // HTTP transfer contract over real PostgreSQL persistence; auth-policy behavior is covered in Authorization/.
    private sealed class TransferAuthorization(string tenant) : IRequestAuthorizationState
    {
        public ResolvedMembership? Membership => new(tenant, StaffRole.Manager, "user", "device", true);
        public AuthorizationSubject? Subject => null;
        public void Set(ResolvedMembership membership, AuthorizationSubject subject) => throw new NotSupportedException();
    }
    private Task<WebApplication> HttpHost(string root, string tenant) => TestHostFactory.StartAsync("Development", builder =>
    {
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<ServerOptions>(o => o.DataRootPath = root);
        builder.Services.AddScoped<ITenantContext>(_ => new Tenant(tenant));
        builder.Services.AddScoped<IRequestAuthorizationState>(_ => new TransferAuthorization(tenant));
        builder.Services.AddDbContext<AnimoraServerDbContext>(o => o.UseNpgsql(pg.ConnectionString));
        builder.Services.AddSingleton<FileStore>(); builder.Services.AddSingleton<ValetKeyService>();
        builder.Services.AddScoped<FileTransferService>();
        builder.Services.AddRateLimiter(ServerRateLimiting.Configure);
        builder.Services.AddAuthorization(o =>
        {
            foreach (string policy in new[] { CoarsePolicies.TenantMember, CoarsePolicies.DeviceActive,
                PermissionCodes.PatientsRead, PermissionCodes.PatientsWrite })
                o.AddPolicy(policy, p => p.RequireAssertion(_ => true));
        });
    }, app => { app.UseAuthorization(); app.UseRateLimiter(); app.MapFileEndpoints(); });
    [PgFact] public async Task UploadOffset_Resend_HashMismatch_AndDownloadHeaders_OverHttp()
    {
        await using (var db = Db(FileStoreTests.Tenant)) await db.Database.MigrateAsync();
        string root = Path.Combine(Path.GetTempPath(), "animora-file-http-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var app = await HttpHost(root, FileStoreTests.Tenant);
            using var client = app.GetTestClient();
            using var declaration = await client.PostAsJsonAsync("/files", new FileDeclaration("note.txt", "text/plain", 5, null));
            Assert.Equal(HttpStatusCode.Created, declaration.StatusCode);
            var json = await declaration.Content.ReadFromJsonAsync<JsonElement>();
            string id = json.GetProperty("fileId").GetString()!, upload = json.GetProperty("uploadId").GetString()!;
            string valet = json.GetProperty("valet").GetString()!;
            client.DefaultRequestHeaders.Add(FileEndpoints.ValetHeader, valet);
            using var wrong = await client.PutAsync($"/files/uploads/{upload}?offset=2", new ByteArrayContent("hello"u8.ToArray()));
            Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
            Assert.Equal("0", wrong.Headers.GetValues("X-Animora-Next-Offset").Single());
            Assert.Contains("FILE_OFFSET_MISMATCH", await wrong.Content.ReadAsStringAsync());
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var accepted = await client.PutAsync($"/files/uploads/{upload}?offset=0", new ByteArrayContent("hello"u8.ToArray()));
                Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
                Assert.Equal("5", accepted.Headers.GetValues("X-Animora-Next-Offset").Single());
            }
            using var changed = await client.PutAsync($"/files/uploads/{upload}?offset=0", new ByteArrayContent("world"u8.ToArray()));
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            using var resume = await client.GetAsync($"/files/uploads/{upload}");
            Assert.Equal(5, (await resume.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nextOffset").GetInt64());
            using var complete = await client.PostAsync($"/files/uploads/{upload}/complete", null);
            Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
            using var wrongOp = await client.GetAsync($"/files/{id}");
            Assert.Equal(HttpStatusCode.Forbidden, wrongOp.StatusCode);
            using var grant = await client.PostAsJsonAsync($"/files/{id}/valet", new { operation = "download" });
            client.DefaultRequestHeaders.Remove(FileEndpoints.ValetHeader);
            client.DefaultRequestHeaders.Add(FileEndpoints.ValetHeader,
                (await grant.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valet").GetString());
            using var download = await client.GetAsync($"/files/{id}");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("hello", await download.Content.ReadAsStringAsync());
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.True(download.Headers.CacheControl!.NoStore); Assert.True(download.Headers.CacheControl.Private);
            client.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 3);
            using var range = await client.GetAsync($"/files/{id}");
            Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode); Assert.Equal("ell", await range.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Range = null;
            await using var foreignApp = await HttpHost(root, "01K00000000000000000000003");
            using var foreign = foreignApp.GetTestClient();
            foreign.DefaultRequestHeaders.Add(FileEndpoints.ValetHeader, client.DefaultRequestHeaders.GetValues(FileEndpoints.ValetHeader));
            using var guessed = await foreign.GetAsync($"/files/{id}"); Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
            using var bad = await client.PostAsJsonAsync("/files", new FileDeclaration("bad.txt", "text/plain", 5, new string('a', 64)));
            var badJson = await bad.Content.ReadFromJsonAsync<JsonElement>();
            string badUpload = badJson.GetProperty("uploadId").GetString()!;
            client.DefaultRequestHeaders.Remove(FileEndpoints.ValetHeader);
            client.DefaultRequestHeaders.Add(FileEndpoints.ValetHeader, badJson.GetProperty("valet").GetString());
            using var chunk = await client.PutAsync($"/files/uploads/{badUpload}?offset=0", new ByteArrayContent("hello"u8.ToArray()));
            using var mismatch = await client.PostAsync($"/files/uploads/{badUpload}/complete", null);
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
            Assert.Contains("FILE_HASH_MISMATCH", await mismatch.Content.ReadAsStringAsync());
            await using var verify = Db(FileStoreTests.Tenant);
            Assert.Equal(FileState.Mismatch, (await verify.FileObjects.SingleAsync(x => x.UploadId == badUpload)).State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

}
