using Animora.Contracts.Visits;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// One selectable row in the rx step's «افزودن خدمت…» / «افزودن کالا…» picker,
/// mirroring the reference's <c>&lt;option&gt;</c> text
/// (<c>&lt;title&gt; — &lt;price&gt;</c>; Animora-UI/assets/visit.js renderRx).
/// The owning combo binds its selection back to the collision-free add command.
/// </summary>
public sealed class RxCatalogOptionViewModel
{
    public RxCatalogOptionViewModel(RxCatalogItem item)
    {
        Item = item;
        Display = $"{item.Title} — {VisitsPresentation.Money(item.Price)}";
    }

    public RxCatalogItem Item { get; }

    public string Display { get; }
}
