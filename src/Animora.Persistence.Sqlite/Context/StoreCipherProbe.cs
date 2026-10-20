using System.Text;
using Microsoft.Data.Sqlite;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>
/// Runtime facts about the native SQLite that is actually loaded and about the store file on disk
/// (SQL-01). A package reference is never proof (STACK.md §3.3): the cipher bundle is detected by asking
/// the loaded native library, so a plain <c>e_sqlite3</c> build — where <c>PRAGMA key</c> is silently a
/// no-op — can never be mistaken for an encrypting one.
/// </summary>
public static class StoreCipherProbe
{
    private static readonly byte[] PlaintextHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");
    private static readonly Lazy<bool> CipherAvailable = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>True when the loaded native SQLite is SQLite3 Multiple Ciphers (exposes <c>sqlite3mc_version()</c>).</summary>
    public static bool IsCipherAvailable() => CipherAvailable.Value;

    /// <summary>
    /// True when <paramref name="path"/> exists and starts with the plain-SQLite magic header, i.e. it is an
    /// unencrypted store. Missing/empty/encrypted files are <c>false</c> (an encrypted file looks like noise).
    /// </summary>
    public static bool IsPlaintextDatabaseFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < PlaintextHeader.Length) return false;
            Span<byte> header = stackalloc byte[16];
            stream.ReadExactly(header);
            return header.SequenceEqual(PlaintextHeader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when <paramref name="path"/> exists and is non-empty (a zero-byte file is a not-yet-created store).</summary>
    public static bool HasContent(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Probe()
    {
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sqlite3mc_version();";
            return command.ExecuteScalar() is string { Length: > 0 };
        }
        catch (SqliteException)
        {
            return false; // "no such function": the loaded native library carries no cipher.
        }
    }
}
