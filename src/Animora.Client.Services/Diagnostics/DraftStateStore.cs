using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Diagnostics;

/// <summary>Persists opt-in UI drafts after a short quiet period.</summary>
public sealed class DraftStateStore(IServiceScopeFactory scopes, IClock clock)
{
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);

    public async Task<Result> SaveAsync(string screenKey, string userId, string payloadJson,
        CancellationToken ct = default)
    {
        var key = $"{userId}\n{screenKey}";
        CancellationTokenSource delay;
        lock (_gate)
        {
            if (_pending.Remove(key, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }
            delay = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _pending[key] = delay;
        }

        try
        {
            await Task.Delay(DebounceDelay, delay.Token);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var draft = await db.Set<UiDraftState>()
                .SingleOrDefaultAsync(e => e.ScreenKey == screenKey && e.UserId == userId, delay.Token);
            if (draft is null)
                db.Add(UiDraftState.Create(screenKey, userId, payloadJson, clock.UtcNow));
            else
                draft.ReplacePayload(payloadJson, clock.UtcNow);
            await db.SaveChangesAsync(delay.Token);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Fail(ErrorCodes.StoreUnavailable, exception.Message);
        }
        finally
        {
            lock (_gate)
            {
                if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, delay))
                    _pending.Remove(key);
            }
            delay.Dispose();
        }
    }

    public async Task<Result<string?>> LoadAsync(string screenKey, string userId, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var payload = await db.Set<UiDraftState>().AsNoTracking()
                .Where(e => e.ScreenKey == screenKey && e.UserId == userId)
                .Select(e => e.PayloadJson)
                .SingleOrDefaultAsync(ct);
            return Result<string?>.Ok(payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result<string?>.Fail(ErrorCodes.StoreUnavailable, exception.Message);
        }
    }

    public async Task<Result> ClearAsync(string screenKey, string userId, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            await db.Set<UiDraftState>()
                .Where(e => e.ScreenKey == screenKey && e.UserId == userId)
                .ExecuteDeleteAsync(ct);
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Fail(ErrorCodes.StoreUnavailable, exception.Message);
        }
    }
}
