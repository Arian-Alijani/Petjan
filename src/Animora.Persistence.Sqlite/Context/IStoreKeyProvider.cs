namespace Animora.Persistence.Sqlite.Context;

/// <summary>Key source for the encrypted-store integration in A11; null means plaintext for now.</summary>
public interface IStoreKeyProvider
{
    string? GetKey();
}

public sealed class NullStoreKeyProvider : IStoreKeyProvider
{
    public string? GetKey() => null;
}
