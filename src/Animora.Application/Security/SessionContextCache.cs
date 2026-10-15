namespace Animora.Application.Security;

/// <summary>
/// App-wide cache of the last known <see cref="SessionSnapshot"/> so the synchronous
/// <see cref="ICurrentUserProvider.BuildQueryContext"/> never touches the store. Written only by the
/// identity handlers and <see cref="SessionCurrentUserProvider"/>; <see langword="null"/> = not yet restored.
/// </summary>
public sealed class SessionContextCache
{
    private SessionSnapshot? _current;

    public SessionSnapshot? Current => Volatile.Read(ref _current);

    public void Set(SessionSnapshot? snapshot) => Volatile.Write(ref _current, snapshot);

    /// <summary>Updates a refresh result only if the exact session is still current (never resurrect sign-out).</summary>
    public bool TryUpdate(SessionSnapshot expected, SessionSnapshot updated) =>
        ReferenceEquals(Interlocked.CompareExchange(ref _current, updated, expected), expected);

    public void MarkLocked()
    {
        while (Current is { UserId: not null } current)
            if (TryUpdate(current, current with { IsLocked = true })) return;
    }
}
