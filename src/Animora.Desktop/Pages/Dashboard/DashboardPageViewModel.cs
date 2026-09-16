using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Identity;
using Animora.Contracts.Navigation;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Dashboard screen ViewModel (Phase 06). Loads the current user's
/// role-specific snapshot from <see cref="IDashboardQueryService"/> and
/// exposes it as bindable collections shared by the three role sub-views
/// (Manager/Cashier/Doctor) — a single ViewModel, per CONVENTIONS.md §3/§5.
/// </summary>
public sealed partial class DashboardPageViewModel : PageViewModelBase
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string _heroTitle = string.Empty;

    [ObservableProperty]
    private string _heroSubtitle = string.Empty;

    [ObservableProperty]
    private string _primaryListTitle = string.Empty;

    [ObservableProperty]
    private string _secondaryListTitle = string.Empty;

    public DashboardPageViewModel(IDashboardQueryService dashboard, ICurrentUserContext currentUser, INavigationService navigation)
    {
        _navigation = navigation;
        Role = currentUser.Role;

        Title = "داشبورد";
        Eyebrow = "مرکز فرمان";
        Breadcrumbs.Add("داشبورد");

        // The mock resolves synchronously (Task.FromResult); a future
        // Datasync-backed implementation would call this from an async
        // OnLoaded hook instead — no ViewModel shape change needed then.
        var snapshot = dashboard.GetSnapshotAsync(Role).GetAwaiter().GetResult();
        Apply(snapshot);
    }

    public UserRole Role { get; }

    public bool IsManager => Role == UserRole.Manager;

    public bool IsCashier => Role == UserRole.Cashier;

    public bool IsDoctor => Role == UserRole.Doctor;

    public ObservableCollection<DashboardStat> Stats { get; } = new();

    public ObservableCollection<DashboardRowViewModel> PrimaryList { get; } = new();

    public ObservableCollection<DashboardRowViewModel> SecondaryList { get; } = new();

    public IReadOnlyList<DashboardQuickAction> QuickActions { get; } = DashboardQuickAction.All;

    /// <summary>Hero CTA target — mirrors the reference's most prominent quick tile ("شروع ویزیت").</summary>
    public DashboardQuickAction PrimaryAction => QuickActions[2];

    [RelayCommand]
    private void OpenQuickAction(DashboardQuickAction? action)
    {
        if (action is null)
        {
            return;
        }

        var page = _navigation.NavigateTo(action.Target);
        if (page is PlaceholderPageViewModel placeholder)
        {
            placeholder.ConfigureFor(action.Label);
        }
    }

    private void Apply(DashboardSnapshot snapshot)
    {
        HeroTitle = snapshot.HeroTitle;
        HeroSubtitle = snapshot.HeroSubtitle;
        PrimaryListTitle = snapshot.PrimaryListTitle;
        SecondaryListTitle = snapshot.SecondaryListTitle;

        Stats.Clear();
        foreach (var stat in snapshot.Stats)
        {
            Stats.Add(stat);
        }

        PrimaryList.Clear();
        foreach (var item in snapshot.PrimaryList.Select(x => new DashboardRowViewModel(x)))
        {
            PrimaryList.Add(item);
        }

        SecondaryList.Clear();
        foreach (var item in snapshot.SecondaryList.Select(x => new DashboardRowViewModel(x)))
        {
            SecondaryList.Add(item);
        }
    }
}
