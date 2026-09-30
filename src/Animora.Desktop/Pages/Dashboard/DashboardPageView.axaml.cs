using Animora.Contracts.Navigation;
using Avalonia.Controls;

namespace Animora.Desktop.Pages.Dashboard;

public partial class DashboardPageView : UserControl
{
    private UserRole? _renderedRole;

    public DashboardPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDashboardContextChanged;
    }

    // Pure presentation selection, no data loading/business rules. Only the active view is constructed;
    // a cached dashboard view reuses that subtree across navigation (no reflection or hidden trees).
    private void OnDashboardContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not DashboardPageViewModel vm)
        {
            RoleContentHost.Content = null;
            _renderedRole = null;
            return;
        }

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
}
