using System.Diagnostics;
using System.IO.Compression;
using Animora.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

public sealed class DiagnosticsBundleTests
{
    [Fact]
    public async Task BundleHasSixMembersAndRedactionNoteWithoutPii()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.InitializeAsync();
        using var scope = fixture.CreateScope();
        var stopwatch = Stopwatch.StartNew();
        var result = await scope.ServiceProvider.GetRequiredService<IDiagnosticsBundleBuilder>().BuildAsync();
        stopwatch.Stop();

        Assert.True(result.IsSuccess, result.Detail);
        using var archive = ZipFile.OpenRead(result.Value!);
        Assert.Contains(archive.Entries, entry => entry.FullName == "environment.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "schema.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "pipeline.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "audit-tail.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "logs/");
        var redaction = Assert.Single(archive.Entries, entry => entry.FullName == "redaction.txt");
        using var reader = new StreamReader(redaction.Open());
        Assert.Contains("excluded or redacted", await reader.ReadToEndAsync(), StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), stopwatch.Elapsed.ToString());
    }
}
