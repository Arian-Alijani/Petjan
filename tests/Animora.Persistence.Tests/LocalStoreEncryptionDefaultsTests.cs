using Animora.Persistence.Sqlite.Options;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>SQL-01: the shipped encryption policy never silently downgrades and enables the cipher when it can.</summary>
public sealed class LocalStoreEncryptionDefaultsTests
{
    [Fact]
    public void FreshStoreWithCipher_EnablesEncryption()
    {
        var options = new LocalStoreOptions();
        var decision = LocalStoreEncryptionDefaults.Resolve(options, cipherAvailable: true, plaintextStoreExists: false, storeFileExists: false);
        Assert.Equal(StoreEncryptionDecision.Enabled, decision);
        Assert.True(options.EncryptionEnabled);
        Assert.True(options.K2Approved);
        Assert.True(options.Validate().IsSuccess);
    }

    [Fact]
    public void FreshStoreWithoutCipher_StaysPlaintextButIsReported()
    {
        var options = new LocalStoreOptions();
        var decision = LocalStoreEncryptionDefaults.Resolve(options, cipherAvailable: false, plaintextStoreExists: false, storeFileExists: false);
        Assert.Equal(StoreEncryptionDecision.PlaintextNoCipherBundle, decision);
        Assert.False(options.EncryptionEnabled);
    }

    [Fact]
    public void ExistingEncryptedStore_RequiresEncryptionEvenWithoutCipher()
    {
        var options = new LocalStoreOptions();
        var decision = LocalStoreEncryptionDefaults.Resolve(options, cipherAvailable: false, plaintextStoreExists: false, storeFileExists: true);
        Assert.Equal(StoreEncryptionDecision.RequiredByExistingStore, decision);
        Assert.True(options.EncryptionEnabled); // fails closed at startup instead of quarantining a healthy encrypted file
    }

    [Fact]
    public void ExistingPlaintextStore_IsNotReKeyedInPlace()
    {
        var options = new LocalStoreOptions();
        var decision = LocalStoreEncryptionDefaults.Resolve(options, cipherAvailable: true, plaintextStoreExists: true, storeFileExists: true);
        Assert.Equal(StoreEncryptionDecision.PlaintextLegacyStore, decision);
        Assert.False(options.EncryptionEnabled);
    }

    [Fact]
    public void ExplicitHostConfiguration_IsLeftUntouched()
    {
        var options = new LocalStoreOptions { EncryptionEnabled = true, K2Approved = true };
        Assert.Equal(StoreEncryptionDecision.ExplicitlyConfigured,
            LocalStoreEncryptionDefaults.Resolve(options, cipherAvailable: false, plaintextStoreExists: false, storeFileExists: false));
    }
}
