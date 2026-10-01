using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Appointments.Export;

/// <summary>A toggleable filter chip (doctor / type / status / preset) in the export dialog.</summary>
public sealed partial class ExportChipViewModel : ObservableObject
{
    public ExportChipViewModel(string id, string label, IBrush accent, bool isOn, string sub = "")
    {
        Id = id;
        Label = label;
        Accent = accent;
        Sub = sub;
        _isOn = isOn;
    }

    public string Id { get; }

    public string Label { get; }

    /// <summary>Secondary line (e.g. a doctor's specialty); empty when not used.</summary>
    public string Sub { get; }

    public bool HasSub => Sub.Length > 0;

    public IBrush Accent { get; }

    [ObservableProperty]
    private bool _isOn;

    /// <summary>Matching appointment count inside the current date selection.</summary>
    [ObservableProperty]
    private string _countText = string.Empty;
}

/// <summary>One cell of the multi-day picker month grid.</summary>
public sealed partial class ExportDayCellViewModel : ObservableObject
{
    public ExportDayCellViewModel(DateOnly date, string label, bool inMonth, bool isToday, bool isFriday)
    {
        Date = date;
        Label = label;
        InMonth = inMonth;
        IsToday = isToday;
        IsFriday = isFriday;
    }

    public DateOnly Date { get; }

    public string Label { get; }

    public bool InMonth { get; }

    public bool IsToday { get; }

    public bool IsFriday { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Appointments on this day (all doctors) — drawn as a small dot/count.</summary>
    [ObservableProperty]
    private int _count;

    public bool HasAppointments => Count > 0;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(HasAppointments));
}
