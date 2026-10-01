using System.ComponentModel;
using Animora.Contracts.Navigation;
using Avalonia.Controls;

namespace Animora.Desktop.Pages.Dashboard;

public partial class DashboardPageView : UserControl
{
    private UserRole? _renderedRole;
    private DashboardPageViewModel? _subscribed;

    public DashboardPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDashboardContextChanged;
    }

    // Pure presentation selection, no data loading/business rules. Only the active view is constructed;
    // a cached dashboard view reuses that subtree across navigation (no reflection or hidden trees).
    // The layout follows the ViewModel's Role *property* (not only DataContext identity): the dashboard
    // ViewModel is a singleton, so a different signed-in role arrives as a property change on the same object.
    private void OnDashboardContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = DataContext as DashboardPageViewModel;
        if (_subscribed is null)
        {
            RoleContentHost.Content = null;
            _renderedRole = null;
            return;
        }

        _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        ApplyRoleLayout(_subscribed);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardPageViewModel.Role) && sender is DashboardPageViewModel vm)
            ApplyRoleLayout(vm);
    }

    private void ApplyRoleLayout(DashboardPageViewModel vm)
    {
        if (_renderedRole != vm.Role)
        {
            RoleContentHost.Content = vm.Role switch
            {
                UserRole.Manager or UserRole.Receptionist => new ManagerDashboardView(),
                UserRole.Cashier => new CashierDashboardView(),
                UserRole.Doctor => new DoctorDashboardView(),
                _ => null,
            };
            _renderedRole = vm.Role;
        }
        if (RoleContentHost.Content is Control view) view.DataContext = vm;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // A cached view detaches on navigation; drop the subscription so it cannot keep reacting off-screen.
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Re-subscribe exactly once (remove-then-add) and catch up with a role change that happened while detached.
        if (_subscribed is null) return;
        _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        ApplyRoleLayout(_subscribed);
    }
}
