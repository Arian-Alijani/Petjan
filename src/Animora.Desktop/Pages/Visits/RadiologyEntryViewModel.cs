using System.Collections.ObjectModel;
using Animora.Contracts.Visits;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// One attached radiology file as a chip: shown removable inside the radiology sheet and as an
/// open-on-click chip in the encounter's radiology list.
/// </summary>
public sealed partial class RadiologyFileViewModel : ObservableObject
{
    private readonly Action<RadiologyFileViewModel>? _remove;

    public RadiologyFileViewModel(RadiologyFile file, Action<RadiologyFileViewModel>? remove = null)
    {
        File = file;
        _remove = remove;
    }

    public RadiologyFile File { get; }

    public string Hash => File.Hash;

    public string Name => File.Name;

    public bool IsImage => File.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Icon-resource key of the chip glyph (image vs. document).</summary>
    public string IconKey => IsImage ? "IconImage" : "IconFile";

    [RelayCommand]
    private void Remove() => _remove?.Invoke(this);
}

/// <summary>One radiology study row of the encounter («رادیولوژی» list): body part, Jalali date, result and files.</summary>
public sealed class RadiologyEntryViewModel
{
    public RadiologyEntryViewModel(RadiologyEntry entry)
    {
        Entry = entry;
        Files = new ObservableCollection<RadiologyFileViewModel>(entry.Files.Select(f => new RadiologyFileViewModel(f)));
    }

    public RadiologyEntry Entry { get; }

    public string Id => Entry.Id;

    public DateTime TakenAt => Entry.TakenAt;

    public string BodyPart => Entry.BodyPart;

    public string DateText => ParaclinicPresentation.DisplayDate(Entry.TakenAt);

    public string Result => Entry.Result ?? string.Empty;

    public bool HasResult => !string.IsNullOrWhiteSpace(Entry.Result);

    public ObservableCollection<RadiologyFileViewModel> Files { get; }

    public bool HasFiles => Files.Count > 0;

    public string FilesCaption => $"پیوست‌ها: {PersianNumberFormatter.ToPersianDigits(Files.Count)}";
}
