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
        Tone = item.TagTone switch
        {
            DashboardTone.Danger => Tone.Danger,
            DashboardTone.Warn => Tone.Warn,
            DashboardTone.Info => Tone.Info,
            DashboardTone.Ok => Tone.Ok,
            _ => Tone.Neutral,
        };
    }

    public string AvatarText { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string TagText { get; }

    public Tone Tone { get; }
}
