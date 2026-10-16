using System;
using System.Diagnostics;

namespace Animora.Desktop.Services;

/// <inheritdoc cref="IFileLauncher"/>
public sealed class FileLauncher : IFileLauncher
{
    public bool Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            // No associated viewer / sandboxed FS: the caller shows a toast.
            return false;
        }
    }
}
