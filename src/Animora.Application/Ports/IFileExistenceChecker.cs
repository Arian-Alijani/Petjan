namespace Animora.Application.Ports;

/// <summary>
/// Application port (Phase S17) that answers whether a stored file is <c>Available</c> in a
/// given tenant, so a command that creates a <c>DocumentReference</c> can validate its
/// <c>FileId</c> inside its own transaction without the Application layer referencing EF Core.
/// Implemented in <c>Animora.Persistence.PostgreSql</c>. A file row is identified by
/// (tenantId, fileId); the bytes themselves are fetched out-of-band with a valet key.
/// </summary>
public interface IFileExistenceChecker
{
    /// <summary>
    /// Returns <see langword="true"/> when a <c>FileObject</c> with the given id exists in the
    /// tenant and is in the <c>Available</c> state; otherwise <see langword="false"/>.
    /// </summary>
    Task<bool> IsAvailableAsync(string tenantId, string fileId, CancellationToken ct = default);
}
