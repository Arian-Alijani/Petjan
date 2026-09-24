namespace Animora.Application.Correlation;

/// <summary>Flows the current <c>CorrelationId</c> (ULID) across async calls.</summary>
public interface ICorrelationContext
{
    /// <summary>The correlation id of the current flow, or <see langword="null"/> outside any scope.</summary>
    string? CurrentId { get; }

    /// <summary>
    /// Begins a scope with <paramref name="id"/>; when null/blank, keeps the current id or mints a new
    /// ULID. Disposing restores the previous id.
    /// </summary>
    IDisposable Begin(string? id = null);
}
