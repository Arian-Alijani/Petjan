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
    /// <summary>
    /// Seeds one <c>Products</c> row carrying money (<c>DefaultCostPrice</c>/<c>DefaultSalePrice</c>) and
    /// quantity (<c>ReorderLevel</c>) value objects — the exact projection shape that used to throw a raw
    /// 500 UNEXPECTED on <c>/sync/batches</c>. The Products insert is journaled by the capture trigger, so
    /// an incremental open from sequence 0 must project (not drop) it. CategoryId has no FK, so no category
    /// row is required; Version/Deleted use their DB defaults.
    /// </summary>
    public async Task SeedProduct()
    {
        await using var db = Factory.Create();
        var product = IdGenerator.NewId(DateTimeOffset.UtcNow);
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO public.""Products""
            (""Id"",""TenantId"",""UpdatedAt"",""Sku"",""Name"",""CategoryId"",""Kind"",""UnitCode"",
             ""IsPrescriptionOnly"",""IsActive"",""TrackStock"",
             ""ReorderLevelScaled"",""ReorderLevelUnit"",
             ""DefaultCostPriceMinor"",""DefaultCostPriceCurrency"",
             ""DefaultSalePriceMinor"",""DefaultSalePriceCurrency"")
            VALUES ({product},{Tenant},now(),'SKU1','Kibble','cat-1',0,'unit',false,true,true,
             5000,'unit',12345,'IRT',20000,'IRT')");
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
