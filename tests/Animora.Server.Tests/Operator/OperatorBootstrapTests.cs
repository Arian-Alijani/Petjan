using Xunit;

namespace Animora.Server.Tests.Operator;

public sealed class OperatorBootstrapTests
{
    [Fact]
    public void OnlyWhenEmpty_AndEnvOnly()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "Animora.Server", "Operator", "OperatorBootstrapper.cs"));
        Assert.Contains("Environment.GetEnvironmentVariable(\"Operator__BootstrapUserName\")", source);
        Assert.Contains("Environment.GetEnvironmentVariable(\"Operator__BootstrapPassword\")", source);
        Assert.DoesNotContain("IConfiguration", source);
        Assert.Contains("db.Accounts.AnyAsync", source);
        Assert.Contains("MustChangePassword = true", source);
        Assert.True(source.IndexOf("db.Accounts.AnyAsync", StringComparison.Ordinal) < source.IndexOf("db.Accounts.Add", StringComparison.Ordinal));
        Assert.Contains("pg_advisory_xact_lock", source);
        Assert.DoesNotContain("LogInformation(password", source);
    }
}
