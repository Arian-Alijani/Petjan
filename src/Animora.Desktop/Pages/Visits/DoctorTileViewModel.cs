using Animora.Contracts.Visits;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// Adapts a <see cref="DoctorRailInfo"/> to one tile on the left doctor rail,
/// mirroring the reference's <c>renderDocRail</c> card (avatar + name +
/// specialty + today's-appointment metric + selection check).
/// </summary>
public sealed partial class DoctorTileViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public DoctorTileViewModel(DoctorRailInfo doctor)
    {
        Doctor = doctor;
        Name = doctor.Name;
        // No specialty field exists on the staff/user records yet, so the rail falls back to the plain role
        // label rather than inventing a specialty.
        Specialty = string.IsNullOrWhiteSpace(doctor.Specialty) ? "پزشک" : doctor.Specialty;
        HasVisitInProgress = doctor.HasVisitInProgress;
        Initials = doctor.Initials;
        AvatarBrush = VisitsPresentation.Fill(doctor.Accent);
        MetricCount = PersianNumberFormatter.ToPersianDigits(doctor.AppointmentCount);
    }

    public DoctorRailInfo Doctor { get; }
    public string Name { get; }
    public string Specialty { get; }
    public string Initials { get; }
    public IBrush AvatarBrush { get; }
    public string MetricCount { get; }

    /// <summary>True while the doctor has an in-progress visit — drives the «در ویزیت» badge.</summary>
    public bool HasVisitInProgress { get; }
}
