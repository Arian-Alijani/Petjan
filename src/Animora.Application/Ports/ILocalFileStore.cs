using Animora.Domain.Results;

namespace Animora.Application.Ports;

/// <summary>
/// Content-addressed local blob store keyed by lowercase hex SHA-256. Metadata lives in <c>DocumentReference</c>.
/// </summary>
public interface ILocalFileStore
{
    /// <summary>Stores the content (deduplicated) and returns its SHA-256 hash.</summary>
    Task<Result<string>> PutAsync(Stream content, CancellationToken ct);

    /// <summary>Opens a read-only stream; the caller disposes it.</summary>
    Task<Result<Stream>> OpenAsync(string hash, CancellationToken ct);

    Task<Result> DeleteAsync(string hash, CancellationToken ct);
}
