using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Animora.Application;
using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Identity;
using Animora.Desktop;
using Animora.Domain.Results;
using Animora.Infrastructure.Local;
using Animora.Infrastructure.Local.Security;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// A standalone manual-test tool, not part of the solution or the shipped desktop application.
// No caller-supplied path, clinic selector, reset, deletion, SQL import, or authentication bypass.
try
{
    if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("This manual-test tool requires Windows/DPAPI.");
    const int MinSeedCount = 1;
    const int MaxSeedCount = 50000;
    if (args.Length != 2 || !Regex.IsMatch(args[0], "\\Arandom-[a-f0-9]{32}\\z"))
        throw new ArgumentException("Expected a NEW random-<GUID> profile and a scenario count.");
    // Accept ANY number the user types (e.g. 55, 9999, 9636362492836), ignoring spaces, separators and a
    // leading sign, then clamp it into the safe supported range instead of failing. This keeps the manual
    // launch resilient to huge or odd inputs while never letting an out-of-range value reach the seeder.
    var rawCount = new string((args[1] ?? string.Empty).Where(char.IsDigit).ToArray());
    if (rawCount.Length == 0)
        throw new ArgumentException("The scenario count must contain at least one digit (e.g. 55, 9999, 250).");
    var requestedText = rawCount.TrimStart('0');
    if (requestedText.Length == 0) requestedText = "0";
    var count = BigInteger.TryParse(rawCount, NumberStyles.None, CultureInfo.InvariantCulture, out var requested)
        ? (int)BigInteger.Min(BigInteger.Max(requested, MinSeedCount), MaxSeedCount)
        : MaxSeedCount; // Anything larger than int/BigInteger parsing can express is treated as "as many as allowed".
    if (requestedText != count.ToString(CultureInfo.InvariantCulture))
        Console.WriteLine($"==> Requested {requestedText} scenarios; clamped to the supported range {MinSeedCount}..{MaxSeedCount} -> {count}.");

    var profile = LocalTestProfile.Parse([$"--local-test={args[0]}"])!;
    // Fail BEFORE composing/opening storage if even an incomplete previous profile exists.
    if (Directory.Exists(profile.DataDirectory) || File.Exists(profile.DataDirectory))
        throw new InvalidOperationException("Refusing to modify an existing profile. Generate a new one with -SeedCount.");
    if (Console.IsInputRedirected) throw new InvalidOperationException("Run from an interactive PowerShell terminal.");
    Console.WriteLine("Create your isolated test Manager (real authentication, no default password).");
    Console.Write("Username [manager]: ");
    var username = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(username)) username = "manager";
    Console.Write("Password (at least 8 characters, letters + digits): ");
    var password = ReadPassword();
    Console.Write("Confirm password: ");
    if (ReadPassword() != password) throw new ArgumentException("Passwords do not match. Nothing was seeded.");
    var policy = PasswordPolicy.Validate(password);
    if (policy.IsFailure) throw new ArgumentException($"Password refused: {policy.Error.Code}. Nothing was seeded.");

    Directory.CreateDirectory(profile.DataDirectory);
    // Atomic ownership token: even two concurrent direct invocations cannot seed the same profile.
    using var reservation = new FileStream(Path.Combine(profile.DataDirectory, "random-data.lock"),
        FileMode.CreateNew, FileAccess.Write, FileShare.None);
    var services = new ServiceCollection();
    services.AddAnimoraLocalInfrastructure(profile.ConfigureStore);
    services.AddSingleton<ISecretStore>(sp => profile.CreateSecretStore(
        sp.GetRequiredService<IOptions<LocalStoreOptions>>(),
        sp.GetRequiredService<ILogger<FileFallbackSecretStore>>()));
    services.AddAnimoraApplication(builder => builder.AddIdentityUseCases());
    await using var provider = services.BuildServiceProvider();
    await using (var scope = provider.CreateAsyncScope())
    {
        Require(await scope.ServiceProvider.GetRequiredService<ILocalStoreInitializer>().InitializeAsync(default));
        var current = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = Require(current.BuildAnonymousCommandContext());
        Require(await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(
            new CompleteFirstRunSetupCommand("Synthetic test clinic", "Test Manager", username, password), context, default));
    }
    password = string.Empty;

    // Reference catalogs come from normal initialization; synthetic business data stays in scripts.
    // Include 32 baseline scenarios so even -SeedCount 1 has data in every major screen.
    await RandomData.PopulateAsync(provider, count + 32);
    await using (var scope = provider.CreateAsyncScope())
    {
        var maintenance = scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();
        Require(await maintenance.RebuildStockSnapshotsAsync(default));
        Require(await maintenance.RebuildFinanceProjectionsAsync(default));
        Require(await maintenance.RebuildActivityFeedAsync(default));
    }
    // Written last: a failed/partial generation must never be launched automatically by clean-run.
    // Store the username (never the password) so clean-run.ps1 can echo the login right before launch.
    await File.WriteAllTextAsync(Path.Combine(profile.DataDirectory, "random-data-complete.json"),
        JsonSerializer.Serialize(new { Profile = profile.Name, Scenarios = count, BaselineScenarios = 32, Username = username, CreatedUtc = DateTimeOffset.UtcNow }));
    Console.WriteLine();
    Console.WriteLine("=====================================================================");
    Console.WriteLine($" Ready: {count:N0} additional scenarios + 32 baseline scenarios.");
    Console.WriteLine(" Sign in to the app that is about to launch with THESE credentials:");
    Console.WriteLine($"   Username : {username}");
    Console.WriteLine("   Password : the password you just typed above (chosen by you).");
    Console.WriteLine($" Isolated storage: {profile.DataDirectory}");
    Console.WriteLine("=====================================================================");
    Console.WriteLine();
    return 0;
}
catch (Exception ex)
{
    // Domain failures are surfaced by Require; never print password-bearing requests or a stack trace.
    Console.Error.WriteLine(ex is ArgumentException or InvalidOperationException
        ? ex.Message : $"Generation failed ({ex.GetType().Name}). No clinic storage was selected.");
    Console.Error.WriteLine("Any partial test profile is kept, not deleted. Rerun -SeedCount to use a NEW profile.");
    return 1;
}

static string ReadPassword()
{
    var text = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return text.ToString(); }
        if (key.Key == ConsoleKey.Escape) throw new ArgumentException("Cancelled. Nothing was seeded.");
        if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
        else if (!char.IsControl(key.KeyChar) && text.Length < 200) text.Append(key.KeyChar);
    }
}

static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! :
    throw new InvalidOperationException($"Test-data operation failed: {result.Error.Code}");
