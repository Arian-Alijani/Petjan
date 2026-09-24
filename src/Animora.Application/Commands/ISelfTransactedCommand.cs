namespace Animora.Application.Commands;

/// <summary>
/// Marks a command whose handler owns its persistence (explicit <c>IUnitOfWork.SaveAsync</c>) instead of the
/// ambient pipeline transaction. Used by authentication commands (B03b): a failed sign-in must still persist
/// its failed-attempt counter and audit row, and the session service writes through its own scope, which an
/// open ambient write transaction would block.
/// </summary>
public interface ISelfTransactedCommand;
