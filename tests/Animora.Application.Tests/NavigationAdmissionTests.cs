using Animora.Contracts.Identity;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.Inventory;
using Animora.Desktop.Pages.Finance;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using Animora.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests
{
    /// <summary>Production navigation/policy source, with inert page bodies (no Avalonia/DB startup).
    /// Capability doubles exercise UI admission only; backend authorization remains independently tested.</summary>
    public sealed class NavigationAdmissionTests
    {
        [Fact]
        public async Task Denial_PrecedesResolveAndConfigure_InEveryOverload()
        {
            var gate = new Gate();
            var toast = new Toast();
            var resolutions = 0;
            using var services = new ServiceCollection().AddSingleton(_ =>
            {
                resolutions++;
                return new InventoryPageViewModel();
            }).BuildServiceProvider();
            var nav = new NavigationService(services, gate, toast);
            var configurations = 0;
            Assert.Null(nav.NavigateTo(typeof(InventoryPageViewModel)));
            Assert.Null(nav.NavigateTo<InventoryPageViewModel>());
            Assert.Null(nav.NavigateTo<InventoryPageViewModel>(_ => configurations++));
            Assert.Null(await nav.NavigateToAsync<InventoryPageViewModel>(_ =>
            {
                configurations++;
                return Task.CompletedTask;
            }));
            Assert.Equal(0, resolutions);
            Assert.Equal(0, configurations);
            Assert.Null(nav.CurrentViewModel);
            Assert.Equal(4, toast.Messages);
            Assert.Null(nav.NavigateTo(typeof(UnknownPage)));
            Assert.Null(nav.NavigateTo(typeof(InventoryPageViewModel), "patients"));
        }

        [Fact]
        public async Task RevokeDuringAwait_DoesNotPublishOrActivate()
        {
            var gate = new Gate { Allowed = { PermissionCodes.InventoryRead } };
            var page = new InventoryPageViewModel();
            using var services = new ServiceCollection().AddSingleton(page).BuildServiceProvider();
            var nav = new NavigationService(services, gate, new Toast());
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = nav.NavigateToAsync<InventoryPageViewModel>(async _ =>
            {
                entered.SetResult();
                await resume.Task;
            });
            await entered.Task;
            gate.Allowed.Clear();
            resume.SetResult();
            Assert.Null(await pending);
            Assert.Null(nav.CurrentViewModel);
            Assert.Equal(0, page.Activations);
            gate.Allowed.Add(PermissionCodes.InventoryRead);
            Assert.Same(page, nav.NavigateTo<InventoryPageViewModel>());
            Assert.Equal(1, page.Activations);
        }

        [Fact]
        public void FinanceSemanticEntry_UsesItsOwnPermission()
        {
            var gate = new Gate { Allowed = { PermissionCodes.FinanceRead } };
            using var services = new ServiceCollection().AddSingleton<FinancePageViewModel>().BuildServiceProvider();
            var nav = new NavigationService(services, gate, new Toast());
            Assert.Null(nav.NavigateTo<FinancePageViewModel>());
            Assert.NotNull(nav.NavigateTo(typeof(FinancePageViewModel), "expense"));
            Assert.Null(nav.NavigateTo(typeof(FinancePageViewModel), "reports"));
            gate.Allowed.Clear();
            gate.Allowed.Add(PermissionCodes.ReportsRead);
            Assert.NotNull(nav.NavigateTo<FinancePageViewModel>());
            Assert.Null(nav.NavigateTo(typeof(FinancePageViewModel), "expense"));
        }

        [Theory]
        [InlineData("purchasing", PermissionCodes.PurchasingRead)]
        [InlineData("reception", PermissionCodes.ReceptionManage)]
        [InlineData("cashier", PermissionCodes.SalesRead)]
        public void NonSidebarTargets_AreExplicit(string key, string permission) =>
            Assert.Equal(permission, NavPermissions.For(key));

        [Fact]
        public void SameSingletonDestination_ActivatesOnce_WithoutLeaveOrChangeEvent()
        {
            var gate = new Gate { Allowed = { PermissionCodes.InventoryRead, PermissionCodes.PatientsRead } };
            var page = new InventoryPageViewModel();
            using var services = new ServiceCollection().AddSingleton(page).BuildServiceProvider();
            var nav = new NavigationService(services, gate, new Toast());
            var changes = 0;
            nav.CurrentViewModelChanged += (_, _) => changes++;
            Assert.Same(page, nav.NavigateTo<InventoryPageViewModel>());
            Assert.Equal((1, 0, 1), (page.Activations, page.Deactivations, changes));
            Assert.Same(page, nav.NavigateTo<InventoryPageViewModel>());
            Assert.Same(page, nav.NavigateTo(typeof(InventoryPageViewModel)));
            Assert.Same(page, nav.NavigateTo<InventoryPageViewModel>(_ => { }));
            Assert.Equal((4, 0, 1), (page.Activations, page.Deactivations, changes));
        }

        [Fact]
        public async Task LeavingAndReturning_DeactivatesOnce_AndReactivatesOnce()
        {
            var gate = new Gate { Allowed = { PermissionCodes.InventoryRead, PermissionCodes.PatientsRead } };
            var inventory = new InventoryPageViewModel();
            var patients = new Animora.Desktop.Pages.Patients.PatientsPageViewModel();
            using var services = new ServiceCollection().AddSingleton(inventory).AddSingleton(patients).BuildServiceProvider();
            var nav = new NavigationService(services, gate, new Toast());
            nav.NavigateTo<InventoryPageViewModel>();
            nav.NavigateTo<Animora.Desktop.Pages.Patients.PatientsPageViewModel>();
            Assert.Equal((1, 1), (inventory.Activations, inventory.Deactivations));
            Assert.Equal((1, 0), (patients.Activations, patients.Deactivations));
            await nav.NavigateToAsync<InventoryPageViewModel>(_ => Task.CompletedTask);
            Assert.Equal((2, 1), (inventory.Activations, inventory.Deactivations));
            Assert.Equal((1, 1), (patients.Activations, patients.Deactivations));
        }

        [Fact]
        public void DeniedNavigation_NeitherActivatesNorDeactivatesTheCurrentPage()
        {
            var gate = new Gate { Allowed = { PermissionCodes.PatientsRead } };
            var patients = new Animora.Desktop.Pages.Patients.PatientsPageViewModel();
            using var services = new ServiceCollection().AddSingleton(patients).AddSingleton<InventoryPageViewModel>().BuildServiceProvider();
            var nav = new NavigationService(services, gate, new Toast());
            nav.NavigateTo<Animora.Desktop.Pages.Patients.PatientsPageViewModel>();
            Assert.Null(nav.NavigateTo<InventoryPageViewModel>());
            Assert.Same(patients, nav.CurrentViewModel);
            Assert.Equal((1, 0), (patients.Activations, patients.Deactivations));
        }

        private sealed class UnknownPage : PageViewModelBase { }
        private sealed class Toast : IToastService
        {
            public int Messages { get; private set; }
            public void Show(string text, Tone tone = Tone.Ok) => Messages++;
        }
        private sealed class Gate : ICapabilityGate
        {
            public HashSet<string> Allowed { get; } = new(StringComparer.Ordinal);
            public bool Can(string permissionCode) => Allowed.Contains(permissionCode);
            public bool CanAny(params string[] codes) => codes.Any(Can);
            public CapabilityState GetState(string permissionCode) => Can(permissionCode) ? CapabilityState.Allowed : CapabilityState.Denied;
            public event EventHandler? CapabilitiesChanged { add { } remove { } }
        }
    }

    public abstract class NavigationTestPage : PageViewModelBase
    {
        public int Activations { get; private set; }
        public int Deactivations { get; private set; }
        public override void OnNavigatedTo() => Activations++;
        public override void OnNavigatedFrom() => Deactivations++;
    }
}

// Inert page bodies replace only screen construction. The policy/service/base are linked unchanged.
namespace Animora.Desktop.Pages.Dashboard { public sealed class DashboardPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Appointments { public sealed class AppointmentsCalendarViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Patients { public sealed class PatientsPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Visits
{
    public sealed class VisitsTriagePageViewModel : Animora.Application.Tests.NavigationTestPage { }
    public sealed class VisitEncounterViewModel : Animora.Application.Tests.NavigationTestPage { }
}
namespace Animora.Desktop.Pages.Vaccination { public sealed class VaccinationPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Boarding { public sealed class BoardingPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Sales { public sealed class SalePageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Inventory { public sealed class InventoryPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Finance { public sealed class FinancePageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Cheques { public sealed class ChequesPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.History { public sealed class HistoryPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Profile { public sealed class ProfilePageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Purchasing { public sealed class PurchasingPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Reception { public sealed class ReceptionPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
namespace Animora.Desktop.Pages.Cashier { public sealed class CashierPageViewModel : Animora.Application.Tests.NavigationTestPage { } }
