using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Visits.Settings;

/// <summary>One laboratory test type row in the Visits settings tab («انواع آزمایش»).</summary>
public sealed partial class LabTestTypeItemViewModel : ObservableObject
{
    private readonly Func<LabTestTypeItemViewModel, Task> _remove;

    public LabTestTypeItemViewModel(string id, string name, string? defaultUnit, Func<LabTestTypeItemViewModel, Task> remove)
    {
        Id = id;
        Name = name;
        DefaultUnit = defaultUnit ?? string.Empty;
        _remove = remove;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>The usual unit; empty when none.</summary>
    public string DefaultUnit { get; }

    public bool HasUnit => DefaultUnit.Length > 0;

    [RelayCommand]
    private Task RemoveAsync() => _remove(this);
}
