namespace Animora.Application.Security;

/// <summary>
/// App-wide cache of the last known <see cref="SessionSnapshot"/> so the synchronous
/// <see cref="ICurrentUserProvider.BuildQueryContext"/> never touches the store. Written only by the
/// identity handlers and <see cref="SessionCurrentUserProvider"/>; <see langword="null"/> = not yet restored.
/// </summary>
public sealed class SessionContextCache
{
    private volatile SessionSnapshot? _current;

    public SessionSnapshot? Current => _current;

    public void Set(SessionSnapshot? snapshot) => _current = snapshot;

    public void MarkLocked()
    {
        if (_current is { UserId: not null } s) _current = s with { IsLocked = true };
    }
}
