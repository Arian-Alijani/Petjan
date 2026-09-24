using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A11c backup/restore: a verified backup restores with pre-backup rows intact, retention keeps
/// exactly <c>BackupRetentionCount</c> files, and every action appends one maintenance-log row.
/// </summary>
public sealed class BackupRestoreTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static ILocalStoreMaintenance Maintenance(LocalStoreTestFixture fixture, IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();

    private static async Task AddInvoice(LocalStoreTestFixture fixture, string number)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var invoice = Invoice.Create(fixture.TenantId, number, InvoiceKind.DirectSale, Base, "U1",
            ownerId: "OWN1").Value!;
        invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(1_000), 0m, 0m, InvoiceLineSource.Manual, null));
        db.Add(invoice);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task BackupThenRestore_KeepsPreBackupRows()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync();
        await AddInvoice(fixture, "INV-BEFORE");

        string backupPath;
        using (var scope = fixture.CreateScope())
        {
            var backup = await Maintenance(fixture, scope).BackupAsync(default);
            Assert.True(backup.IsSuccess, backup.Detail);
            backupPath = backup.Value!;
            Assert.True(File.Exists(backupPath));
        }

        // Mutate after the backup, then restore: the post-backup row is gone, the pre-backup one stays.
        await AddInvoice(fixture, "INV-AFTER");
        using (var scope = fixture.CreateScope())
        {
            var restore = await Maintenance(fixture, scope).RestoreAsync(backupPath, default);
            Assert.True(restore.IsSuccess, restore.Detail);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var numbers = await db.Set<Invoice>().Select(i => i.InvoiceNumber).ToListAsync();
            Assert.Contains("INV-BEFORE", numbers);
            Assert.DoesNotContain("INV-AFTER", numbers);
        }
    }

    [Fact]
    public async Task EveryAction_AppendsOneMaintenanceLogRow()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync();

        using (var scope = fixture.CreateScope())
        {
            Assert.True((await Maintenance(fixture, scope).BackupAsync(default)).IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var logs = await db.Set<StoreMaintenanceLog>()
                .Where(l => l.Kind == StoreMaintenanceKind.Backup).ToListAsync();
            Assert.Single(logs);
        }
    }

    [Fact]
    public async Task Retention_KeepsExactlyTheConfiguredCount()
    {
        using var fixture = new LocalStoreTestFixture(configureOptions: options => options.BackupRetentionCount = 2);
        await fixture.InitializeAsync();

        for (var i = 0; i < 3; i++)
        {
            using var scope = fixture.CreateScope();
            Assert.True((await Maintenance(fixture, scope).BackupAsync(default)).IsSuccess);
        }

        var backups = Directory.GetFiles(fixture.BackupDirectory, "animora-*.db");
        Assert.Equal(2, backups.Length);
    }
}
