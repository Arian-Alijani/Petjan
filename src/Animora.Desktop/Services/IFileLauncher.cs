namespace Animora.Desktop.Services;

/// <summary>
/// Hands a local file to the operating system's default viewer (e.g. an X-ray image or a PDF report). A seam so
/// ViewModels never touch <c>Process</c> directly and tests can substitute it.
/// </summary>
public interface IFileLauncher
{
    /// <summary>Opens <paramref name="path"/> with the default application; <see langword="false"/> when none could be started.</summary>
    bool Open(string path);
}
