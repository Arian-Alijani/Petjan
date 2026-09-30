using System.Text.RegularExpressions;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>Source is embedded at build time: guards cannot silently inspect a different checkout.</summary>
public sealed class StartupBudgetTests
{
    [Fact]
    public void FirstFrame_DefersAllStoreWork_ToOpenedEventAndReadinessGate()
    {
        using var stream = typeof(StartupBudgetTests).Assembly.GetManifestResourceStream("Startup.App.cs");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var source = Regex.Replace(reader.ReadToEnd(), @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
        var startup = Body(source, "public override void OnFrameworkInitializationCompleted()");
        Assert.Contains("desktop.MainWindow = login;", startup);
        Assert.Contains("login.Opened += OnLoginOpened;", startup);
        foreach (var token in new[] { "Migrate", "EnsureCreated", "Seed", "Rebuild", "InitializeLocalStoreAsync",
            ".Wait(", ".Result", "GetAwaiter().GetResult" })
            Assert.DoesNotContain(token, startup);
        var opened = Body(source, "private static async void OnLoginOpened");
        Assert.Contains("await InitializeLocalStoreAsync()", opened);
        var store = Body(source, "private static async Task<bool> InitializeLocalStoreAsync()");
        Assert.Contains("await Task.Run", store);
        Assert.True(store.IndexOf("Task.Run", StringComparison.Ordinal) < store.IndexOf("initializer.InitializeAsync", StringComparison.Ordinal));
        Assert.Contains("GetRequiredService<ILocalStoreInitializer>()", store);
        Assert.Contains("GetRequiredService<StoreReadinessGate>().MarkReady()", store);
        Assert.Contains("AddSingleton<IStoreReadinessGate>", source);
    }

    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        start = source.IndexOf('{', start);
        var depth = 1;
        var end = start + 1;
        for (; end < source.Length && depth > 0; end++)
        {
            if (source[end] == '{') depth++;
            else if (source[end] == '}') depth--;
        }
        Assert.Equal(0, depth);
        return source[(start + 1)..(end - 1)];
    }
}
