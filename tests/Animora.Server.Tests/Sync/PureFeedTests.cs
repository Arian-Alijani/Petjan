using System.IO.Compression;
using System.Text.Json;
using Animora.Contracts.Wire.Sync;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Commands;
using Animora.Server.Sync;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;
namespace Animora.Server.Tests.Sync;

public sealed class RetentionPolicyTests
{
    [Fact] public void JournalRetention_Is_OfflineWindowPlus30() =>
        Assert.Equal(RetentionPolicy.JournalRetentionDays, new OfflineWindowOptions().MaxOfflineDays + 30);
    [Fact] public void SameConstantSizesCommandIntakeRetention() =>
        Assert.Equal(RetentionPolicy.JournalRetentionDays, new CommandIntakeRetentionOptions().FullRecordDays);
}
public sealed class OpenBatchDecisionTests
{
    [Fact]
    public void EpochChanged_BeyondRetention_UpToDate_Ready()
    {
        var p = new FeedPosition(2, 1, 10, 3);
        Assert.Equal("EPOCH_CHANGED", BatchFreezer.Decide(new(1, 3), p)!.Resync!.Reason);
        Assert.Equal("BEYOND_RETENTION", BatchFreezer.Decide(new(2, 2), p)!.Resync!.Reason);
        Assert.Equal("UpToDate", BatchFreezer.Decide(new(2, 10), p)!.Kind);
        Assert.Null(BatchFreezer.Decide(new(2, 3), p));
    }
}
public sealed class RecordProjectorTests
{
    [Fact]
    public void ProjectionOrder_ParentsFirst_DeletesLast()
    {
        var names = SyncContractCatalog.ProjectionOrder.Select(x => x.Name).ToList();
        Assert.True(names.IndexOf("Owner") < names.IndexOf("Pet"));
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs"));
        Assert.Contains("CASE WHEN r.\"Id\" IS NULL THEN 1 ELSE 0 END", source);
        Assert.Contains("ChildImage(child.Table)", source);
    }
    [Fact]
    public void Projection_StripsTenantAndUnknownFields_Recursively()
    {
        using var doc = JsonDocument.Parse("""{"Name":"product","TenantId":"foreign","PasswordHash":"secret","Variants":[{"TenantId":"hidden","VariantId":"v"}]}""");
        var json = RecordProjector.Project("Product", doc.RootElement).GetRawText();
        Assert.DoesNotContain("TenantId", json); Assert.DoesNotContain("hidden", json); Assert.DoesNotContain("secret", json);
    }
    /// <summary>
    /// D17: a LIVE row that merely fails to project — an unparseable/missing Version or Deleted, or a throw from
    /// <c>Project</c> — must be skipped for the batch, never emitted as a tombstone. A 'D' would make every
    /// replica delete real business data that is still live on the server. The projector may build a 'D' in
    /// exactly one place: the <c>reader.IsDBNull(1)</c> branch, where the LEFT JOIN found no live row at all.
    /// </summary>
    [Fact]
    public void FaultyRow_IsSkipped_NeverTombstoned()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs"));
        Assert.Contains("if (reader.IsDBNull(1)) { yield return new(d.Name, id, 'D', null, null, true, null); continue; }", source);
        Assert.Equal(1, source.Split("'D', null, null, true, null").Length - 1);
        Assert.Contains("catch (Exception ex)", source);
        Assert.Contains("projection fault", source);
        Assert.Contains("unparseable Version/Deleted", source);
    }
}
public sealed class PageWriterTests
{
    [Fact]
    public async Task PagesNeverExceed256KiB_Uncompressed()
    {
        string root = Path.Combine(RepoRoot.Find(), ".sandbox/test-artifacts", Guid.NewGuid().ToString("N"));
        var writer = new FeedPageWriter(Options.Create(new FeedStorageOptions { DataRootPath = root }));
        var work = writer.CreateWorkspace(); var tenant = "01AAAAAAAAAAAAAAAAAAAAAAAA"; var batch = "01BBBBBBBBBBBBBBBBBBBBBBBB";
        try
        {
            var pages = await writer.WriteAsync(tenant, batch, work, Records(), CancellationToken.None);
            Assert.True(pages.Count > 1);
            await foreach (var page in pages.ReadMetadata())
            {
                await using var file = File.OpenRead(Path.Combine(writer.BatchDirectory(tenant, batch), $"{page.PageOrdinal}.gz"));
                await using var gzip = new GZipStream(file, CompressionMode.Decompress); using var memory = new MemoryStream();
                await gzip.CopyToAsync(memory); Assert.True(memory.Length <= FeedStorageOptions.PageTargetBytes);
            }
            var again = await writer.WriteAsync(tenant, batch, work, Records(), CancellationToken.None);
            Assert.Equal(pages.ContentHash, again.ContentHash);
        }
        finally { Directory.Delete(root, true); }
    }
    private static async IAsyncEnumerable<SyncRecord> Records()
    {
        for (int i = 0; i < 1000; i++) { yield return new("Owner", i.ToString(), 'U', null, null, false,
            JsonSerializer.SerializeToElement(new { fullName = new string('a', 600) })); await Task.Yield(); }
    }
    [Fact]
    public void TempFileThenAtomicMove()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Persistence.PostgreSql/Replication/FeedPageWriter.cs"));
        Assert.Contains("FileMode.CreateNew", source); Assert.Contains("File.Move(temporary, final)", source);
        Assert.DoesNotContain("File.Move(temporary, final, true)", source);
    }
}
public sealed class GcOrderTests
{
    [Fact]
    public void EachStep_Idempotent_CrashBetweenSteps_Safe()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Persistence.PostgreSql/Replication/FeedRetentionService.cs"));
        Assert.True(source.IndexOf("FeedBatchState.Retired", StringComparison.Ordinal) < source.IndexOf("Directory.Delete(dir, true)", StringComparison.Ordinal));
        Assert.True(source.IndexOf("Directory.Delete(dir, true)", StringComparison.Ordinal) < source.IndexOf("Set<FeedBatchPageRow>().Where", StringComparison.Ordinal));
        Assert.Contains("SetProperty(f => f.OldestRetainedSequence, floor)", source);
        Assert.Contains("await tx.CommitAsync(ct)", source);
    }
}
public sealed class SyncEndpointsTests
{
    [Fact]
    public async Task ContractVersionUnsupported_Returns426()
    {
        var http = new DefaultHttpContext(); http.Request.Headers[Animora.Contracts.Wire.WireContract.HeaderName] = "1";
        var invocation = new DefaultEndpointFilterInvocationContext(http);
        var result = await new SyncContractFilter().InvokeAsync(invocation, _ => throw new InvalidOperationException("Gate did not reject."));
        Assert.Equal(426, ((IStatusCodeHttpResult)result!).StatusCode);
    }
    [Fact]
    public void ForeignBatchId_Returns404_Audited()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Server/Sync/SyncEndpoints.cs"));
        Assert.Contains("audit.WriteAsync", source); Assert.Contains("sync.batch.not_found", source); Assert.Contains("Results.NotFound()", source);
    }
}
