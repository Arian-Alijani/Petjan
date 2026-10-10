using Animora.Domain.Results;

namespace Animora.Sync.Abstractions;

/// <summary>A normalised replication scope name (e.g. "default"); the unit a pull targets.</summary>
public readonly record struct SyncScope
{
    private readonly string? _name;

    private SyncScope(string name) => _name = name;

    /// <summary>The scope name; a default-constructed value normalises to "default" (never null).</summary>
    public string Name => _name ?? "default";

    /// <summary>The single scope used today; equals the literal "default" (matches SyncCursor.DefaultScope).</summary>
    public static readonly SyncScope Default = new("default");

    /// <summary>Validates and creates a scope: 1–64 chars of [a-z0-9._-], else a validation failure.</summary>
    public static Result<SyncScope> Create(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 64 || !IsLegal(trimmed))
            return Result<SyncScope>.Fail(ErrorCodes.ValidationFailed, "Sync scope must be 1–64 chars of a-z 0-9 . _ -.");
        return Result<SyncScope>.Ok(new SyncScope(trimmed));
    }

    private static bool IsLegal(string value)
    {
        foreach (var c in value)
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '.' or '_' or '-'))
                return false;
        return true;
    }

    /// <summary>Returns the scope name.</summary>
    public override string ToString() => Name;
}
