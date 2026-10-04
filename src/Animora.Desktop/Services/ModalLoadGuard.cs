using System;
using System.Threading.Tasks;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.Patients;

namespace Animora.Desktop.Services;

/// <summary>
/// Runs the initial data load of a modal that was opened by a constructor-time fire-and-forget call.
/// A backend failure would otherwise be lost (the modal stays blank and the user sees nothing happen); here it
/// becomes a danger toast and the empty modal is closed (X30). The load itself stays a plain awaited
/// <see cref="Task"/> so it remains testable.
/// </summary>
public static class ModalLoadGuard
{
    /// <summary>
    /// Awaits <paramref name="load"/>; on a typed backend failure shows <paramref name="fallback"/> (or the
    /// access-lost copy) as a danger toast and closes the modal, never throwing onto the dispatcher.
    /// </summary>
    public static async Task RunAsync(IToastService toast, IDialogService dialogs, string fallback, Func<Task> load)
    {
        ArgumentNullException.ThrowIfNull(toast);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(load);

        try
        {
            await load();
        }
        catch (InvalidOperationException ex)
        {
            toast.Show(PatientsPresentation.CommandFailureMessage(ex, fallback), Tone.Danger);
            dialogs.Close();
        }
    }
}
