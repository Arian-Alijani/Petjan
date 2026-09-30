using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class UiDraftStore(AnimoraLocalDbContext db) : IUiDraftStore
{
    public async Task SaveAsync(string screenKey, string userId, string payload, DateTimeOffset now, CancellationToken ct)
    {
        var draft = await db.Set<UiDraftState>().AsTracking()
            .SingleOrDefaultAsync(e => e.ScreenKey == screenKey && e.UserId == userId, ct);
        if (draft is null) db.Add(UiDraftState.Create(screenKey, userId, payload, now));
        else draft.ReplacePayload(payload, now);
        await db.SaveChangesAsync(ct);
    }
    public Task<string?> LoadAsync(string screenKey, string userId, CancellationToken ct) =>
        db.Set<UiDraftState>().Where(e => e.ScreenKey == screenKey && e.UserId == userId)
            .Select(e => e.PayloadJson).SingleOrDefaultAsync(ct);
    public async Task ClearAsync(string screenKey, string userId, CancellationToken ct) =>
        await db.Set<UiDraftState>().Where(e => e.ScreenKey == screenKey && e.UserId == userId).ExecuteDeleteAsync(ct);
}
