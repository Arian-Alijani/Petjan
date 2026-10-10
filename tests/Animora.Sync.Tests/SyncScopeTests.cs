using Animora.Domain.Results;
using Animora.Sync.Abstractions;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Facts for <see cref="SyncScope"/> normalisation and validation.</summary>
public sealed class SyncScopeTests
{
    [Fact]
    public void Default_EqualsLiteralDefault_MatchingSyncCursorDefaultScope() =>
        Assert.Equal("default", SyncScope.Default.Name);

    [Fact]
    public void DefaultStruct_IsDefaultScope_NeverNull()
    {
        var scope = default(SyncScope);
        Assert.NotNull(scope.Name);
        Assert.Equal("default", scope.Name);
        Assert.Equal(SyncScope.Default, scope);
    }

    [Fact]
    public void Create_RejectsEmptyTooLongAndIllegalChars()
    {
        foreach (var bad in new[] { "", "   ", new string('a', 65), "BAD!" })
        {
            var result = SyncScope.Create(bad);
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
        }

        var ok = SyncScope.Create("pets");
        Assert.True(ok.IsSuccess);
        Assert.Equal("pets", ok.Value.Name);
    }
}
