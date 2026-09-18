using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales.Shared;

/// <summary>
/// The reusable themed confirm dialog (mirrors the reference's <c>openConfirm</c>
/// / <c>.sw-modal.sw-confirm</c>): a tone-tinted icon tile, a title, a body (with
/// an optional emphasized segment) and a two-button foot whose confirm button
/// turns danger-red when the tone is <c>danger</c>. Other Sale/POS flows reuse
/// it for their yes/no prompts.
/// </summary>
public sealed partial class ConfirmDialogViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly Action? _onConfirm;

    public ConfirmDialogViewModel(
        IDialogService dialogs,
        string tone,
        string iconKey,
        string title,
        string bodyLead,
        string bodyStrong,
        string bodyTrail,
        string bodyNote,
        string confirmLabel,
        string cancelLabel,
        Action? onConfirm)
    {
        _dialogs = dialogs;
        Tone = tone;
        IconKey = iconKey;
        Title = title;
        BodyLead = bodyLead;
        BodyStrong = bodyStrong;
        BodyTrail = bodyTrail;
        BodyNote = bodyNote;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        _onConfirm = onConfirm;

        (IcoBackground, IcoForeground) = SalePresentation.ConfirmIco(tone);
    }

    /// <summary>danger | warn | brand | neutral — the reference's <c>sw-confirm-{tone}</c>.</summary>
    public string Tone { get; }

    public string IconKey { get; }

    public string Title { get; }

    public string BodyLead { get; }

    public string BodyStrong { get; }

    public string BodyTrail { get; }

    public string BodyNote { get; }

    public bool HasBodyNote => !string.IsNullOrEmpty(BodyNote);

    public string ConfirmLabel { get; }

    public string CancelLabel { get; }

    public bool IsDanger => Tone == "danger";

    public IBrush IcoBackground { get; }

    public IBrush IcoForeground { get; }

    [RelayCommand]
    private void Confirm()
    {
        _dialogs.Close();
        _onConfirm?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
