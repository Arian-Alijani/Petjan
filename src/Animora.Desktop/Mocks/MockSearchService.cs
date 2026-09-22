using Animora.Contracts.Patients;
using Animora.Contracts.Search;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Boarding;
using Animora.Desktop.Pages.Cheques;
using Animora.Desktop.Pages.Dashboard;
using Animora.Desktop.Pages.Finance;
using Animora.Desktop.Pages.Inventory;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Pages.Profile;
using Animora.Desktop.Pages.Sales;
using Animora.Desktop.Pages.Vaccination;
using Animora.Desktop.Pages.Visits;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory <see cref="ISearchService"/> for the Ctrl+K command palette. The
/// static command actions are transcribed 1:1 from the reference's
/// <c>CMDK_ACTIONS</c> (Animora-UI/assets/layout.js) — same titles, subtitles,
/// tones, icon mapping and Persian keywords — and the live patient/owner matches
/// are derived from <see cref="IPatientsQueryService"/> so a query actually
/// searches the directory.
/// </summary>
public sealed class MockSearchService : ISearchService
{
    // Beyond this many live patient/owner hits the palette stays focused on the
    // curated command actions (the reference's list stays short and scannable).
    private const int MaxLiveResults = 6;

    // Queries shorter than this are treated as command-only (avoids a flood of
    // hits on a single common letter).
    private const int MinLiveQueryLength = 2;

    private readonly IPatientsQueryService _patients;

    private readonly IReadOnlyList<ActionSeed> _actions = BuildActions();

    public MockSearchService(IPatientsQueryService patients) => _patients = patients;

    public IReadOnlyList<SearchResult> DefaultSuggestions { get; } =
        BuildActions().Take(4).Select(a => a.ToResult()).ToArray();

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            // No query → the default suggestions, no directory round-trip.
            return DefaultSuggestions;
        }

        var results = await Task.Run(async () =>
        {
            var matches = new List<SearchResult>();

            // 1. Static command actions filtered by title + subtitle + keywords
            //    (case-insensitive; Persian strings compare ordinally, which is
            //    correct for exact keyword matching).
            foreach (var action in _actions)
            {
                ct.ThrowIfCancellationRequested();
                if (action.Matches(trimmed))
                {
                    matches.Add(action.ToResult());
                }
            }

            // 2. Live patient/owner matches — only once the query is meaningful.
            if (trimmed.Length >= MinLiveQueryLength)
            {
                var live = await SearchDirectoryAsync(trimmed, ct).ConfigureAwait(false);
                matches.AddRange(live);
            }

            return (IReadOnlyList<SearchResult>)matches;
        }, ct).ConfigureAwait(false);

        return results;
    }

    private async Task<List<SearchResult>> SearchDirectoryAsync(string query, CancellationToken ct)
    {
        var terms = new[] { query };
        var live = new List<SearchResult>(MaxLiveResults);

        var pets = await _patients.GetPetsAsync().ConfigureAwait(false);
        var owners = await _patients.GetOwnersAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // Owners whose name/phone/id matches — a pet's subtitle prefers its
        // owner name, so index owners by id for a cheap lookup.
        var ownersById = new Dictionary<string, OwnerSummary>(StringComparer.Ordinal);
        foreach (var owner in owners)
        {
            ownersById[owner.Id] = owner;
        }

        foreach (var pet in pets)
        {
            if (live.Count >= MaxLiveResults)
            {
                break;
            }

            if (Contains(pet.Name, terms) || Contains(pet.Code, terms) || Contains(pet.MicrochipCode, terms))
            {
                var ownerName = ownersById.TryGetValue(pet.OwnerId, out var owner)
                    ? owner.FirstName + " " + owner.LastName
                    : "بیمار";
                live.Add(new SearchResult(
                    pet.Name,
                    ownerName + " — " + SpeciesLabel(pet.Species),
                    "IconUsers",
                    "teal",
                    typeof(PatientsPageViewModel),
                    pet.Id));
            }
        }

        foreach (var owner in owners)
        {
            if (live.Count >= MaxLiveResults)
            {
                break;
            }

            if (Contains(owner.FirstName, terms) || Contains(owner.LastName, terms)
                || Contains(owner.Mobile1, terms) || Contains(owner.Mobile2, terms)
                || Contains(owner.Phone, terms) || Contains(owner.File, terms))
            {
                live.Add(new SearchResult(
                    owner.FirstName + " " + owner.LastName,
                    "مالک — " + owner.File,
                    "IconUsers",
                    "teal",
                    typeof(PatientsPageViewModel),
                    owner.Id));
            }
        }

        return live;
    }

    // Case-insensitive, Persian-aware substring match against any term.
    private static bool Contains(string? haystack, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrEmpty(haystack))
        {
            return false;
        }

        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SpeciesLabel(PetSpecies species) => species switch
    {
        PetSpecies.Cat => "گربه",
        PetSpecies.Dog => "سگ",
        PetSpecies.Bird => "پرنده",
        PetSpecies.Rabbit => "خرگوش",
        PetSpecies.Rodent => "جونده",
        _ => "حیوان",
    };

    // The static command actions, transcribed verbatim from CMDK_ACTIONS
    // (layout.js) with the reference icon names mapped to IconXxx resource keys.
    private static IReadOnlyList<ActionSeed> BuildActions() =>
    [
        new("جستجوی بیمار", "پرونده‌ها و مالکین", "IconUsers", "teal",
            "بیمار مالک پرونده سگ گربه patient", typeof(PatientsPageViewModel)),
        new("ثبت نوبت جدید", "برنامه‌ریزی مراجعه", "IconCalendar", "blue",
            "نوبت تقویم رزرو appointment", typeof(AppointmentsCalendarViewModel)),
        new("صدور فاکتور", "فروش و صندوق", "IconReceipt", "teal",
            "فاکتور فروش صندوق پرداخت invoice", typeof(SalePageViewModel)),
        new("مشاهده گزارش فروش", "هوش کسب‌وکار", "IconChart", "blue",
            "گزارش آمار تحلیل report", typeof(FinancePageViewModel)),
        new("شروع ویزیت", "ثبت معاینه و نسخه", "IconClipboard", "violet",
            "ویزیت معاینه نسخه پرونده پزشکی", typeof(VisitsTriagePageViewModel)),
        new("واکسیناسیون", "یادآورها و برنامه", "IconSyringe", "green",
            "واکسن یادآور ایمن‌سازی", typeof(VaccinationPageViewModel)),
        new("پانسیون", "اقامت و نگهداری حیوانات", "IconHome", "violet",
            "پانسیون اقامت نگهداری اتاق تخلیه boarding", typeof(BoardingPageViewModel)),
        new("ثبت اقامت جدید", "پذیرش پانسیون", "IconPlus", "teal",
            "ثبت اقامت پذیرش پانسیون ورود", typeof(BoardingPageViewModel)),
        new("انبار و کالا", "کنترل موجودی و خرید", "IconBox", "amber",
            "انبار کالا موجودی دارو خرید تأمین سفارش", typeof(InventoryPageViewModel)),
        new("ثبت هزینه", "مدیریت هزینه‌ها", "IconWallet", "amber",
            "هزینه مالی پرداخت", typeof(FinancePageViewModel)),
        new("مدیریت چک", "خزانه‌داری و سررسیدها", "IconCheque", "teal",
            "چک صیاد سررسید وصول برگشتی بانک cheque", typeof(ChequesPageViewModel)),
        new("ثبت چک جدید", "دریافتی یا پرداختی", "IconPlus", "green",
            "ثبت چک جدید دریافتی پرداختی", typeof(ChequesPageViewModel)),
        new("داشبورد", "نمای کلی کلینیک", "IconDashboard", "teal",
            "داشبورد خانه نمای کلی", typeof(DashboardPageViewModel)),
        new("پروفایل و تنظیمات", "حساب کاربری و تنظیمات", "IconShield", "violet",
            "پروفایل تنظیمات حساب کاربری امنیت notification اعلان تم", typeof(ProfilePageViewModel)),
    ];

    /// <summary>One static command action (the reference's CMDK_ACTIONS entry).</summary>
    private sealed record ActionSeed(
        string Title,
        string Subtitle,
        string IconKey,
        string Tone,
        string Keywords,
        Type Target)
    {
        public bool Matches(string query) =>
            Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Keywords.Contains(query, StringComparison.OrdinalIgnoreCase);

        public SearchResult ToResult() => new(Title, Subtitle, IconKey, Tone, Target);
    }
}
