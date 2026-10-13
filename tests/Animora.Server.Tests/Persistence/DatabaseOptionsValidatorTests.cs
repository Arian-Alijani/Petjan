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

/// <summary>IMP-01 / IMP-09: profile-scaled pool defaults and validated tuning knobs.</summary>
public sealed class DatabaseProfileScalingTests
{
    private static readonly DatabaseOptionsValidator Validator = new();

    [Theory]
    [InlineData(2, DatabaseOptionsValidator.Budget2GiB)]
    [InlineData(4, DatabaseOptionsValidator.Budget4GiB)]
    public void ProfileDefaults_ExactlyFillTheProfileBudget_AndValidate(int profile, int budget)
    {
        var (write, read, keyRing, worker) = DatabaseOptions.ForProfile(profile);

        Assert.Equal(budget, write + read + keyRing + worker);
        Assert.True(Validator.Validate(null, new DatabaseOptions
        {
            ProfileGiB = profile, OltpWriteMax = write, OltpReadMax = read, KeyRingMax = keyRing, WorkerMax = worker,
        }).Succeeded);
    }

    [Fact]
    public void FourGiBProfile_ScalesEveryPool_AboveTheTwoGiBDefaults()
    {
        var small = DatabaseOptions.ForProfile(2);
        var large = DatabaseOptions.ForProfile(4);

        Assert.True(large.OltpWrite > small.OltpWrite);
        Assert.True(large.OltpRead > small.OltpRead);
        Assert.True(large.Worker > small.Worker);
    }

    [Theory]
    [InlineData(nameof(DatabaseOptions.SyncAdmissionMax), 65)]
    [InlineData(nameof(DatabaseOptions.WorkerAdmissionMax), 17)]
    [InlineData(nameof(DatabaseOptions.SyncAdmissionWaitSeconds), 0)]
    [InlineData(nameof(DatabaseOptions.SyncAdmissionQueueMax), 1025)]
    [InlineData(nameof(DatabaseOptions.CommandTimeoutSeconds), 4)]
    [InlineData(nameof(DatabaseOptions.CommandTimeoutSeconds), 301)]
    [InlineData(nameof(DatabaseOptions.ConnectionIdleLifetimeSeconds), 9)]
    [InlineData(nameof(DatabaseOptions.ConnectionPruningIntervalSeconds), 0)]
    public void TuningKnobs_AreRangeValidated(string property, int value)
    {
        var options = new DatabaseOptions();
        typeof(DatabaseOptions).GetProperty(property)!.SetValue(options, value);

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(property, StringComparison.Ordinal));
    }

    [Fact]
    public void AutoAdmission_IsZero_AndDefaultsAreValid()
    {
        var options = new DatabaseOptions();

        Assert.Equal(0, options.SyncAdmissionMax);
        Assert.Equal(0, options.WorkerAdmissionMax);
        Assert.True(Validator.Validate(null, options).Succeeded);
    }
}
