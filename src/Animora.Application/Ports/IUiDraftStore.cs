namespace Animora.Application.Ports;

/// <summary>Persistence seam for opt-in UI drafts; presentation never references EF entities.</summary>
public interface IUiDraftStore
{
    Task SaveAsync(string screenKey, string userId, string payload, DateTimeOffset now, CancellationToken ct);
    Task<string?> LoadAsync(string screenKey, string userId, CancellationToken ct);
    Task ClearAsync(string screenKey, string userId, CancellationToken ct);
}
