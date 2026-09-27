using Animora.Domain.Results;

namespace Animora.Application.Diagnostics;

/// <summary>Builds a redacted local support bundle and returns its absolute zip path.</summary>
public interface IDiagnosticsBundleBuilder
{
    Task<Result<string>> BuildAsync(CancellationToken ct = default);
}
