using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Files;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Files;
using Animora.Server.Hosting;
using Animora.Server.Workers;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
namespace Animora.Server.Tests.Files;

public sealed class ReconciliationTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact] public async Task Abandoned_BlobMissing_ZeroRef_Orphan_Bitrot_AreHandled()
    {
        var binding = new AuthTenantContext(); binding.Bind(FileStoreTests.Tenant);
        var options = new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using var db = new AnimoraServerDbContext(options,binding); await db.Database.MigrateAsync();
        string root = Path.Combine(Path.GetTempPath(),"animora-reconcile-"+Guid.NewGuid().ToString("N"));
        var store = new FileStore(Options.Create(new ServerOptions { DataRootPath = root }));
        await using var source = NpgsqlDataSource.Create(pg.ConnectionString);
        try
        {
            var service = new FileTransferService(db,store,TimeProvider.System);
            var staged = (await service.DeclareAsync("u",new("idle.txt","text/plain",5,null),default)).Value!;
            staged.UpdatedUtc = DateTimeOffset.UtcNow.AddHours(-25); await db.SaveChangesAsync();
            var row = (await service.DeclareAsync("u",new("live.txt","text/plain",5,null),default)).Value!;
            await service.WriteAsync(row.UploadId!,0,"hello"u8.ToArray(),default); await service.CompleteAsync(row.UploadId!,default);
            await File.WriteAllTextAsync(store.Blob(row.Sha256!),"corrupt");
            var services = new ServiceCollection(); services.AddScoped<AuthTenantContext>(); services.AddScoped<ITenantContext>(s=>s.GetRequiredService<AuthTenantContext>());
            services.AddScoped<WorkerExecutionContext>(); services.AddDbContext<AnimoraServerDbContext>(o=>o.UseNpgsql(pg.ConnectionString));
            await using var provider = services.BuildServiceProvider(); using var admission = new SemaphoreBackgroundAdmission();
            using var worker = new FileReconciliationWorker(provider.GetRequiredService<IServiceScopeFactory>(),admission,new FileMaintenanceStore(source),store,
                TimeProvider.System,new WorkerHeartbeat(TimeProvider.System),NullLogger<FileReconciliationWorker>.Instance,new Animora.Server.Observability.ServerMetrics());
            await worker.RunOnceAsync(); db.ChangeTracker.Clear();
            Assert.Equal(FileState.Abandoned,(await db.FileObjects.SingleAsync(x=>x.Id==staged.Id)).State);
            Assert.Equal(FileState.Mismatch,(await db.FileObjects.SingleAsync(x=>x.Id==row.Id)).State);
            Assert.Contains(await db.SecurityAudit.ToListAsync(),x=>x.Action=="files.reconcile.bitrot");
            var missing = (await service.DeclareAsync("u",new("missing.txt","text/plain",6,null),default)).Value!;
            await service.WriteAsync(missing.UploadId!,0,"absent"u8.ToArray(),default); await service.CompleteAsync(missing.UploadId!,default);
            File.Delete(store.Blob(missing.Sha256!)); await worker.RunOnceAsync(); db.ChangeTracker.Clear();
            Assert.Equal(FileState.Mismatch,(await db.FileObjects.SingleAsync(x=>x.Id==missing.Id)).State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [PgFact] public async Task GcOrder_CrashSafe_Idempotent()
    {
        var binding = new AuthTenantContext(); binding.Bind(FileStoreTests.Tenant);
        await using var db = new AnimoraServerDbContext(new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(pg.ConnectionString).Options,binding);
        await db.Database.MigrateAsync(); string hash = new('b',64);
        db.BlobRefs.Add(new() { Sha256 = hash,RefCount=0,SizeBytes=1,FirstSeenUtc=DateTimeOffset.UtcNow.AddDays(-2),UnreferencedUtc=DateTimeOffset.UtcNow.AddDays(-2) }); await db.SaveChangesAsync();
        await using var source = NpgsqlDataSource.Create(pg.ConnectionString); var maintenance = new FileMaintenanceStore(source);
        await Assert.ThrowsAsync<IOException>(()=>maintenance.CollectAsync(hash,DateTimeOffset.UtcNow,()=>throw new IOException("simulated crash"),false,default));
        Assert.Equal(1,await db.BlobRefs.CountAsync(x=>x.Sha256==hash));
        Assert.True(await maintenance.CollectAsync(hash,DateTimeOffset.UtcNow,()=>Task.CompletedTask,false,default));
        Assert.False(await maintenance.CollectAsync(hash,DateTimeOffset.UtcNow,()=>Task.CompletedTask,false,default));
    }
}
