using Animora.Domain.Results;

namespace Animora.Persistence.Sqlite.Maintenance;

/// <summary>Opens, verifies and migrates the local store. Implemented by <c>LocalStoreStartup</c> (A11d).</summary>
public interface ILocalStoreInitializer
{
    Task<Result<LocalStoreDiagnostics>> InitializeAsync(CancellationToken ct);
}
