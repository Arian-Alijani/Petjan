using Animora.Contracts.Dashboard;
using Animora.Desktop.Controls;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Adapts a <see cref="DashboardListItem"/> (Contracts DTO, tone-agnostic of
/// the Desktop control library) to the Desktop <see cref="Tone"/> enum that
/// <see cref="Badge"/> binds to, so Views can bind <c>Tone</c> directly
/// without a converter.
/// </summary>
public sealed class DashboardRowViewModel
{
    public DashboardRowViewModel(DashboardListItem item)
    {
        AvatarText = item.AvatarText;
        Title = item.Title;
        Subtitle = item.Subtitle;
        TagText = item.TagText;
        Tone = ToneMap.From(item.TagTone);
    }

    public string AvatarText { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string TagText { get; }

    public Tone Tone { get; }
}
