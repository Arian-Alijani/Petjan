namespace Animora.Application.Dispatch;

/// <summary>Whether a registry entry is a command (transactional) or a query (never transactional).</summary>
public enum RequestKind
{
    Command,
    Query,
}
