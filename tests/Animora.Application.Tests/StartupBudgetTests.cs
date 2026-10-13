using System.Text.RegularExpressions;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>Deterministic source guard. Sources are embedded at build time, not located by cwd at runtime.</summary>
public sealed class StartupBudgetTests
{
    [Fact]
    public void StoreWork_IsDeferredUntilOpened_AndReleasedThroughReadinessGate()
    {
        using var stream = typeof(StartupBudgetTests).Assembly.GetManifestResourceStream("C11.AppStartup")!;
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        var startup = Method(source, "public override void OnFrameworkInitializationCompleted()");
        var registrations = Method(source, "public static void ConfigureServices(IServiceCollection services, LocalTestProfile? testProfile, ActiveTenantStore? tenantStore)");
        var opened = Method(source, "private static async void OnLoginOpened");
        var initialize = Method(source, "private static async Task<bool> InitializeLocalStoreAsync(IServiceProvider provider, CancellationToken ct)");
        var forbidden = @"\b(?:Migrate(?:Async)?|EnsureCreated(?:Async)?|Seed(?:Async)?|Rebuild\w*|InitializeLocalStoreAsync)\s*\(";
        Assert.DoesNotMatch(forbidden, startup);
        Assert.DoesNotMatch(forbidden, registrations);
        var showLogin = Method(source, "private void ShowLogin");
        Assert.Contains("ShowLogin(desktop)", startup);
        Assert.Contains("login.Opened += OnLoginOpened", showLogin);
        Assert.Contains("desktop.MainWindow = login", showLogin);
        Assert.Contains("online || await InitializeLocalStoreAsync(Services, CancellationToken.None)", opened);
        Assert.Contains("Task.Run", initialize);
        Assert.Contains("GetRequiredService<ILocalStoreInitializer>()", initialize);
        Assert.True(initialize.IndexOf("initializer.InitializeAsync", StringComparison.Ordinal) <
                    initialize.IndexOf("StoreReadinessGate>().MarkReady()", StringComparison.Ordinal));
        Assert.Contains("AddSingleton<IStoreReadinessGate>", registrations);
        Assert.DoesNotMatch(@"\.(?:Wait|Result)\b|GetAwaiter\s*\(", startup);
    }

    private static string Method(string source, string signature)
    {
        // Strip comments so an explanatory comment cannot hide/create a forbidden call.
        source = Regex.Replace(source, @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
        var offset = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(offset >= 0, signature);
        var start = source.IndexOf('{', offset);
        var depth = 0;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}' && --depth == 0) return source[(start + 1)..i];
        }
        throw new InvalidOperationException(signature);
    }
}
