using System.Text;
using Animora.Persistence.Sqlite.Maintenance;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>BAK-01: the passphrase-wrapped store-key sidecar round-trips, never carries the key in clear, and fails closed.</summary>
public sealed class StoreKeyEscrowTests
{
    private const string Key = "c3RvcmUta2V5LXBhc3NwaHJhc2UtMzItYnl0ZXM=";

    [Fact]
    public void WrapThenUnwrap_RoundTrips_AndKeyIsNotInTheSidecar()
    {
        var wrapped = StoreKeyEscrow.ExportWrappedKey(Key, "owner passphrase");

        Assert.DoesNotContain(Key, Encoding.UTF8.GetString(wrapped), StringComparison.Ordinal);
        var unwrapped = StoreKeyEscrow.ImportWrappedKey(wrapped, "owner passphrase");
        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(Key, unwrapped.Value);
    }

    [Fact]
    public void WrongPassphrase_FailsClosed()
    {
        var wrapped = StoreKeyEscrow.ExportWrappedKey(Key, "right");
        Assert.True(StoreKeyEscrow.ImportWrappedKey(wrapped, "wrong").IsFailure);
        Assert.True(StoreKeyEscrow.ImportWrappedKey(wrapped, string.Empty).IsFailure);
    }

    [Fact]
    public void DamagedSidecar_FailsClosed()
    {
        Assert.True(StoreKeyEscrow.ImportWrappedKey(Encoding.UTF8.GetBytes("not json"), "x").IsFailure);
    }
}
