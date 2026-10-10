using Animora.Persistence.PostgreSql.Options;
using Xunit;

namespace Animora.Server.Tests.Persistence;

public sealed class DatabaseOptionsValidatorTests
{
    private static readonly DatabaseOptionsValidator Validator = new();

    [Fact]
    public void RejectsPoolSumOver12_On2GiB()
    {
        var result = Validator.Validate(null, new DatabaseOptions { OltpWriteMax = 8, OltpReadMax = 4, KeyRingMax = 1, ProfileGiB = 2 });
        Assert.True(result.Failed);
    }

    [Fact]
    public void Allows12_On2GiB_AndDefaults()
    {
        Assert.True(Validator.Validate(null, new DatabaseOptions()).Succeeded);
        Assert.True(Validator.Validate(null, new DatabaseOptions { OltpWriteMax = 6, OltpReadMax = 3, KeyRingMax = 1, WorkerMax = 2 }).Succeeded);
    }

    [Fact]
    public void Allows20_On4GiB()
    {
        var result = Validator.Validate(null, new DatabaseOptions { OltpWriteMax = 12, OltpReadMax = 5, KeyRingMax = 1, WorkerMax = 2, ProfileGiB = 4 });
        Assert.True(result.Succeeded);
        Assert.True(Validator.Validate(null, new DatabaseOptions { OltpWriteMax = 13, OltpReadMax = 5, KeyRingMax = 1, WorkerMax = 2, ProfileGiB = 4 }).Failed);
    }

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(8, 0, 1)]
    [InlineData(8, 4, 0)]
    public void RejectsNonPositivePools(int write, int read, int keyRing)
    {
        Assert.True(Validator.Validate(null, new DatabaseOptions { OltpWriteMax = write, OltpReadMax = read, KeyRingMax = keyRing }).Failed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void RejectsUnknownProfile(int profile)
    {
        Assert.True(Validator.Validate(null, new DatabaseOptions { ProfileGiB = profile }).Failed);
    }
}
