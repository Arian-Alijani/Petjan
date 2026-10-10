using Animora.Domain.Results;
namespace Animora.Persistence.PostgreSql.Files;

public static class FileLifecycle
{
    private static readonly HashSet<(FileState, FileState)> Edges =
    [
        (FileState.Staged, FileState.Uploaded), (FileState.Uploaded, FileState.HashVerified),
        (FileState.Uploaded, FileState.Mismatch), (FileState.HashVerified, FileState.Committed),
        (FileState.Committed, FileState.Available), (FileState.Committed, FileState.Mismatch),
        (FileState.Available, FileState.Mismatch), (FileState.Staged, FileState.Abandoned),
        (FileState.Uploaded, FileState.Abandoned)
    ];
    public static Result EnsureAllowed(FileState from, FileState to) => Edges.Contains((from, to))
        ? Result.Ok() : Result.Fail(ErrorCodes.FileHashMismatch, "Forbidden file lifecycle transition.");
}
