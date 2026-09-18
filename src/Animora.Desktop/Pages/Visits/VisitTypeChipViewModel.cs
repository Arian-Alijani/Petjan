using Avalonia.Media;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// One filter chip above the booked list, mirroring the reference's
/// <c>bookedListHtml</c> chip row (label + count, active state per the current
/// <c>S.apptFilter</c>). Label/count brushes are pre-resolved so the active
/// (brand-filled) chip reads white while the rest read ink-2/muted.
/// </summary>
public sealed class VisitTypeChipViewModel
{
    public VisitTypeChipViewModel(string filterId, string label, string countText, bool isActive)
    {
        FilterId = filterId;
        Label = label;
        CountText = countText;
        IsActive = isActive;
        LabelBrush = isActive ? Brushes.White : VisitsPresentation.Fill("#344054");
        CountBrush = isActive ? Brushes.White : VisitsPresentation.Fill("#344054");
    }

    public string FilterId { get; }
    public string Label { get; }
    public string CountText { get; }
    public bool IsActive { get; }
    public IBrush LabelBrush { get; }
    public IBrush CountBrush { get; }
}
