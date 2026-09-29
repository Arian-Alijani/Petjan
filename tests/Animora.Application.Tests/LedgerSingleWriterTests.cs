using Animora.Application.Finance;
using Xunit;

namespace Animora.Application.Tests;

public sealed class LedgerSingleWriterTests
{
    [Fact]
    public void ApplicationAndClient_DoNotCreateJournalEntriesOutsidePostingHandler()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "Animora.Application")))
            root = root.Parent;
        Assert.NotNull(root);
        var allowed = Path.GetFullPath(Path.Combine(root.FullName, "src/Animora.Application/Finance/LedgerPostingHandler.cs"));
        Assert.True(File.Exists(allowed));

        var files = new[] { "Animora.Application", "Animora.Client.Services" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root.FullName, "src", project), "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) &&
                           !path.Contains("/bin/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            if (Path.GetFullPath(file) == allowed) continue;
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("JournalEntry.Create(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JournalEntry.CreateReversal(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ledger.Add(", source, StringComparison.Ordinal);
        }
    }
}
