using System.IO.Compression;
using System.Text.Json;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
namespace Animora.Server.Tests.Sync;

internal sealed class SyncPgRig : IAsyncDisposable
{
    public string Tenant { get; } = IdGenerator.NewId(DateTimeOffset.UtcNow);
    public string Root { get; } = Path.Combine(RepoRoot.Find(), ".sandbox/test-artifacts", Guid.NewGuid().ToString("N"));
    public NpgsqlDataSource Source { get; }
    public IFeedContextFactory Factory { get; }
    public FeedPageWriter Writer { get; }
    public FeedStorageOptions Settings { get; }
    public SemaphoreBackgroundAdmission Admission { get; } = new(1);
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    public SyncPgRig(string connection)
    {
        Source = NpgsqlDataSource.Create(connection);
        Factory = new FeedContextFactory(Source, new FixedTenantContext(Tenant), _services);
        Settings = new() { DataRootPath = Root };
        Writer = new(Options.Create(Settings));
    }
    public BatchFreezer Freezer => new(Factory, Admission, new RecordProjector(), Writer, Options.Create(Settings));
    public async Task Initialize()
    {
        await using var db = Factory.Create(); await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.\"Tenants\" (\"Id\",\"TenantId\",\"DisplayName\",\"Timezone\",\"DefaultCurrency\",\"Status\",\"UpdatedAt\") VALUES ({Tenant},{Tenant},'Clinic','Asia/Tehran','IRT',0,now())");
    }
    public async Task<List<SyncRecord>> Download(BatchReady batch)
    {
        var records = new List<SyncRecord>(); var reader = new FeedPageReader(Factory, Writer);
        for (int i = 0; i < batch.PageCount; i++)
        {
            var page = (await reader.OpenAsync(batch.BatchId, i, CancellationToken.None))!;
            await using var content = page.Content; await using var gzip = new GZipStream(content, CompressionMode.Decompress);
            var dto = (await JsonSerializer.DeserializeAsync<SyncPage>(gzip, SyncSerializerV2.Options))!;
            records.AddRange(dto.Records);
        }
        return records;
    }
    public async ValueTask DisposeAsync()
    {
        await Source.DisposeAsync(); await _services.DisposeAsync(); Admission.Dispose();
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
