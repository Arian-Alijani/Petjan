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
        IconKey = ResolveIconKey(item);
    }

    /// <summary>
    /// A semantic dashboard glyph chosen from the same reference icon family.
    /// The source contract deliberately remains UI-agnostic; this presentation
    /// adapter recognizes the dashboard row patterns it renders.
    /// </summary>
    public string IconKey { get; }

    public string AvatarText { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string TagText { get; }

    public Tone Tone { get; }

    private static string ResolveIconKey(DashboardListItem item)
    {
        if (item.Title.Contains("چک", System.StringComparison.Ordinal))
        {
            return "IconCheque";
        }

        if (item.Title.Contains("واکسن", System.StringComparison.Ordinal))
        {
            return "IconSyringe";
        }

        if (item.Title.Contains("پانسیون", System.StringComparison.Ordinal))
        {
            return "IconBriefcase";
        }

        if (item.Title.Contains("کالا", System.StringComparison.Ordinal)
            || item.Title.Contains("موجودی", System.StringComparison.Ordinal))
        {
            return "IconAlert";
        }

        if (item.Title.Contains("گربه", System.StringComparison.Ordinal))
        {
            return "IconCat";
        }

        if (item.Title.Contains("سگ", System.StringComparison.Ordinal))
        {
            return "IconDog";
        }

        return "IconPaw";
    }
}
