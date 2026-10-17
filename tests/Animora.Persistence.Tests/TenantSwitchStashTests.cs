using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// Multi-clinic switch on one installation (user requirement: log into clinic A, then clinic B on the same machine; A's
/// unsynced data must be preserved and isolated, never leaked into B, and restored when the user returns to A).
///
/// The single-tenant store keeps one clinic live at a time. <see cref="ILocalStoreMaintenance.EnsureBoundToAsync(string,bool,System.Threading.CancellationToken)"/>
/// now parks the outgoing clinic's WHOLE store under a per-tenant stash before switching (only on an explicit confirm), and
/// restores that stash when binding back — so returning to clinic A brings back its unsynced command to finish syncing, while
/// clinic B never sees A's rows.
/// </summary>
public sealed class TenantSwitchStashTests
{
    private const string ClinicA = "01TENANTAAAAAAAAAAAAAAAAAA";
    private const string ClinicB = "01TENANTBBBBBBBBBBBBBBBBBB";

    private static ILocalStoreMaintenance Maintenance(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();

    private static CommandEnvelope Envelope(string tenantId, string operationId) => CommandEnvelope.Create(
        operationId, tenantId, "U1", "D1", "sales.invoice.post", "Invoice", "INV-A", "{}", "fp-" + operationId,
        DateTimeOffset.UnixEpoch, localSequence: 1);

    private static async Task EnqueuePendingAsync(LocalStoreTestFixture fixture, string tenantId, string operationId)
    {
        using var scope = fixture.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
        var outcome = await uow.ExecuteAsync(async ct =>
        {
            await journal.EnqueueAsync(Envelope(tenantId, operationId), ct);
            return Result<bool>.Ok(true);
        });
        Assert.True(outcome.IsSuccess, outcome.Detail);
    }

    [Fact]
    public async Task ConfirmedSwitch_PreservesOutgoingUnsyncedData_AndRestoresItOnReturn_WithoutLeakingAcrossClinics()
    {
        // followInstallation: the tenant filter follows LocalInstallationState, so EnsureBoundToAsync's bind drives isolation.
        using var fixture = new LocalStoreTestFixture(followInstallation: true);
        await fixture.InitializeAsync();

        // Bind to clinic A and record one unsynced (LocallyPosted) command for it.
        using (var scope = fixture.CreateScope())
            Assert.True((await Maintenance(scope).EnsureBoundToAsync(ClinicA, default)).IsSuccess);
        await EnqueuePendingAsync(fixture, ClinicA, "OP-A1");

        // Without a confirm, switching away from a clinic with unsynced work is refused and the count is surfaced.
        using (var scope = fixture.CreateScope())
        {
            var refused = await Maintenance(scope).EnsureBoundToAsync(ClinicB, preserveOutgoingUnsynced: false, default);
            Assert.True(refused.IsFailure);
            Assert.Equal("SYNC_PENDING_CHANGES", refused.Error.Code);
            Assert.Equal("tenant_switch_requires_settlement:1", refused.Detail);
        }

        // Confirmed switch: A is parked, B comes up empty of A's data.
        using (var scope = fixture.CreateScope())
            Assert.True((await Maintenance(scope).EnsureBoundToAsync(ClinicB, preserveOutgoingUnsynced: true, default)).IsSuccess);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(ClinicB, db.CurrentTenantId);
            Assert.Equal(0, await db.Set<PendingCommand>().IgnoreQueryFilters().CountAsync());
        }

        // Return to A: the parked store (and its unsynced command) is restored intact.
        using (var scope = fixture.CreateScope())
            Assert.True((await Maintenance(scope).EnsureBoundToAsync(ClinicA, preserveOutgoingUnsynced: true, default)).IsSuccess);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(ClinicA, db.CurrentTenantId);
            var pending = await db.Set<PendingCommand>().IgnoreQueryFilters().ToListAsync();
            Assert.Single(pending);
            Assert.Equal("OP-A1", pending[0].OperationId);
            Assert.Equal(ClinicA, pending[0].TenantId);
        }

        // The consumed stash for A is removed after a successful restore (only the latest parked state is ever kept).
        var stashDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fixture.DatabasePath))!, "clinics", ClinicA);
        Assert.False(Directory.Exists(stashDir));
    }

    [Fact]
    public async Task SwitchingToAFreshClinic_WithNoOutgoingUnsyncedData_StillWipesToAnEmptyStore()
    {
        using var fixture = new LocalStoreTestFixture(followInstallation: true);
        await fixture.InitializeAsync();

        using (var scope = fixture.CreateScope())
            Assert.True((await Maintenance(scope).EnsureBoundToAsync(ClinicA, default)).IsSuccess);
        using (var scope = fixture.CreateScope())
            Assert.True((await Maintenance(scope).EnsureBoundToAsync(ClinicB, default)).IsSuccess);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(ClinicB, db.CurrentTenantId);
            Assert.Equal(0, await db.Set<PendingCommand>().IgnoreQueryFilters().CountAsync());
        }
    }
}
