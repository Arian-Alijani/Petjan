using Animora.Persistence.Sqlite.Context;

namespace Animora.Persistence.Sqlite.Options;

/// <summary>How the shipped composition root resolved at-rest encryption for the local store (SQL-01).</summary>
public enum StoreEncryptionDecision
{
    /// <summary>Not resolved by the shipped defaults (library default or explicit host configuration).</summary>
    Unresolved = 0,
    /// <summary>Host configured encryption explicitly; left untouched.</summary>
    ExplicitlyConfigured,
    /// <summary>Cipher bundle present and the store is new/encrypted: encryption switched on.</summary>
    Enabled,
    /// <summary>Encrypted store file found: encryption is required and the open fails closed if the cipher is missing.</summary>
    RequiredByExistingStore,
    /// <summary>The loaded native SQLite has no cipher; the store stays plaintext (loud warning at startup).</summary>
    PlaintextNoCipherBundle,
    /// <summary>A pre-existing plaintext store; kept as-is until an explicit plaintext-to-encrypted export runs.</summary>
    PlaintextLegacyStore,
}

/// <summary>
/// The encryption policy of the shipped desktop composition (SQL-01). Never silently downgrades an
/// encrypted store and never leaves encryption off when it could be on for a fresh store.
/// </summary>
public static class LocalStoreEncryptionDefaults
{
    /// <summary>
    /// Resolves <see cref="LocalStoreOptions.EncryptionEnabled"/>/<see cref="LocalStoreOptions.K2Approved"/> for the
    /// shipped app from facts about the loaded native library and the store file. Safe to use as the
    /// <c>configure</c> callback of the local-store registration.
    /// </summary>
    public static void ApplyShipped(LocalStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EncryptionDecision = Resolve(options, StoreCipherProbe.IsCipherAvailable(),
            StoreCipherProbe.IsPlaintextDatabaseFile(options.DatabasePath), StoreCipherProbe.HasContent(options.DatabasePath));
    }

    /// <summary>Pure decision function (testable without a native cipher): mutates the flags and returns the decision.</summary>
    public static StoreEncryptionDecision Resolve(LocalStoreOptions options, bool cipherAvailable,
        bool plaintextStoreExists, bool storeFileExists)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.EncryptionEnabled) return StoreEncryptionDecision.ExplicitlyConfigured;

        if (plaintextStoreExists)
            return StoreEncryptionDecision.PlaintextLegacyStore;

        if (storeFileExists)
        {
            // Non-plaintext, non-empty-looking file: it is an encrypted store. Demand the cipher so a missing
            // bundle surfaces as a clear fail-closed startup error instead of "corrupt" -> quarantine.
            options.EncryptionEnabled = true;
            options.K2Approved = true;
            return StoreEncryptionDecision.RequiredByExistingStore;
        }

        if (!cipherAvailable) return StoreEncryptionDecision.PlaintextNoCipherBundle;

        options.EncryptionEnabled = true;
        options.K2Approved = true;
        return StoreEncryptionDecision.Enabled;
    }
}
