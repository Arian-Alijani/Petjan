using Animora.Desktop.Controls;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients.Settings;

/// <summary>
/// ViewModel for the Patients &amp; Owners <b>settings</b> tab.
/// <para>
/// Two concerns, matching the request:
/// <list type="bullet">
///   <item>Animal species &amp; breed management — the existing taxonomy is seeded
///   from <see cref="PatientsPresentation"/> and can be renamed, extended with new
///   breeds/species, and have breeds/species removed.</item>
///   <item>A toggle to enable/disable the "file opened" SMS notification — wired to
///   a UI state and a toast, left as a ready-to-complete scaffold (no SMS backend
///   is called yet).</item>
/// </list>
/// Everything is UI-state only for now; the species list intentionally mirrors the
/// real taxonomy shape so a persistence service can replace the seeding without a
/// view change.
/// </para>
/// </summary>
public sealed partial class PatientsSettingsViewModel : ObservableObject
{
    private readonly IToastService _toast;

    public PatientsSettingsViewModel(IToastService toast)
    {
        _toast = toast;
        SeedSpecies();
    }

    public RangeObservableCollection<SpeciesSettingsItemViewModel> Species { get; } = [];

    /// <summary>Draft text for the "add species" input.</summary>
    [ObservableProperty]
    private string _newSpeciesName = string.Empty;

    // ── SMS "file opened" notification toggle (scaffold, ready to complete) ──

    /// <summary>
    /// Whether an SMS is sent to the owner when a new file is opened. Persisted
    /// only in memory for now; the actual gateway call is a TODO to be completed.
    /// </summary>
    [ObservableProperty]
    private bool _fileOpenedSmsEnabled;

    /// <summary>The message template previewed/edited for the "file opened" SMS.</summary>
    [ObservableProperty]
    private string _fileOpenedSmsTemplate =
        "سلام، پرونده دامپزشکی {نام حیوان} در کلینیک ما با موفقیت ثبت شد.";

    public string FileOpenedSmsStatusText =>
        FileOpenedSmsEnabled ? "فعال" : "غیرفعال";

    partial void OnFileOpenedSmsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(FileOpenedSmsStatusText));
        // TODO: persist the preference and (de)register the "file opened" SMS hook
        // once the SMS gateway integration lands. This toggle is the ready UI seam.
        _toast.Show(
            value
                ? "پیامک باز شدن پرونده فعال شد (در انتظار اتصال سامانه پیامک)"
                : "پیامک باز شدن پرونده غیرفعال شد",
            value ? Tone.Ok : Tone.Neutral);
    }

    [RelayCommand]
    private void SaveSmsTemplate()
    {
        // TODO: persist the template alongside the toggle when the gateway lands.
        _toast.Show("قالب پیامک ذخیره شد (نمونه طراحی)", Tone.Ok);
    }

    // ── species add / remove ───────────────────────────────────────────────
    [RelayCommand]
    private void AddSpecies()
    {
        var value = (NewSpeciesName ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return;
        }

        if (Species.Any(s => string.Equals(s.Name, value, StringComparison.OrdinalIgnoreCase)))
        {
            NewSpeciesName = string.Empty;
            _toast.Show("این نوع حیوان از قبل وجود دارد", Tone.Info);
            return;
        }

        AddSpeciesItem(new SpeciesSettingsItemViewModel(
            species: null, name: value, iconKey: "IconPaw", breeds: []));
        NewSpeciesName = string.Empty;
        _toast.Show("نوع حیوان جدید اضافه شد", Tone.Ok);
    }

    private void SeedSpecies()
    {
        foreach (var species in PatientsPresentation.SpeciesOrder)
        {
            AddSpeciesItem(new SpeciesSettingsItemViewModel(
                species,
                PatientsPresentation.SpeciesLabel(species),
                PatientsPresentation.SpeciesIconKey(species),
                PatientsPresentation.Breeds(species)));
        }
    }

    private void AddSpeciesItem(SpeciesSettingsItemViewModel item)
    {
        item.RemoveRequested += OnSpeciesRemoveRequested;
        Species.Add(item);
    }

    private void OnSpeciesRemoveRequested(SpeciesSettingsItemViewModel item)
    {
        item.RemoveRequested -= OnSpeciesRemoveRequested;
        Species.Remove(item);
    }
}
