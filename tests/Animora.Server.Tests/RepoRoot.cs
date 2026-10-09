namespace Animora.Server.Tests;

internal static class RepoRoot
{
    /// <summary>Walks up from the test binary directory to the folder holding <c>Animora.sln</c>.</summary>
    public static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Animora.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Animora.sln not found above " + AppContext.BaseDirectory);
    }
}
