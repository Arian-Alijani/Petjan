using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// D15: the invoice-detail settlement verdict must surface the ROOT CAUSE of a refusal, not whatever command
/// happened to run last. Reading the single highest-<c>LocalSequence</c> command meant a later accepted or pending
/// money command displaced an earlier rejected post, and a downstream <c>INVOICE_INVALID_TRANSITION</c> displaced
/// the original <c>STOCK_WOULD_GO_NEGATIVE</c> — so the operator saw a symptom, or "settled", for an invoice the
/// server had actually refused.
/// </summary>
public sealed class InvoiceSettlementVerdictTests
{
    private const string InvoiceId = "INV-1";

    /// <summary>A later accepted command of a DIFFERENT type does not supersede an earlier rejected post.</summary>
    [Fact]
    public async Task EarlierRejectedPost_OutranksALaterAcceptedCommand()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedPostedInvoiceAsync(fixture);
        await SeedCommandAsync(fixture, CommandTypes.SalesInvoicePost, sequence: 1,
            SettlementState.Rejected, ErrorCodes.StockWouldGoNegative.Code);
        await SeedCommandAsync(fixture, CommandTypes.SalesPaymentRegister, sequence: 2, SettlementState.Settled);

        var detail = await DetailAsync(fixture);

        Assert.Equal(SettlementStatus.Rejected, detail.SettlementStatus);
        Assert.Equal(ErrorCodes.StockWouldGoNegative.Code, detail.LastSettlementErrorCode);
    }

    /// <summary>An earlier rejected post still wins over a later pending retry of the same operation.</summary>
    [Fact]
    public async Task EarlierRejectedPost_OutranksALaterPendingRetry()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedPostedInvoiceAsync(fixture);
        await SeedCommandAsync(fixture, CommandTypes.SalesInvoicePost, sequence: 1,
            SettlementState.Rejected, ErrorCodes.StockWouldGoNegative.Code);
        await SeedCommandAsync(fixture, CommandTypes.SalesInvoicePost, sequence: 2,
            SettlementState.PendingSettlement);

        var detail = await DetailAsync(fixture);

        Assert.Equal(SettlementStatus.Rejected, detail.SettlementStatus);
        Assert.Equal(ErrorCodes.StockWouldGoNegative.Code, detail.LastSettlementErrorCode);
    }

    /// <summary>A rejection that the SAME operation later settled is resolved, not reported forever.</summary>
    [Fact]
    public async Task SettledRetry_ResolvesTheEarlierRejection()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedPostedInvoiceAsync(fixture);
        await SeedCommandAsync(fixture, CommandTypes.SalesInvoicePost, sequence: 1,
            SettlementState.Rejected, ErrorCodes.StockWouldGoNegative.Code);
        await SeedCommandAsync(fixture, CommandTypes.SalesInvoicePost, sequence: 2, SettlementState.Settled);

        var detail = await DetailAsync(fixture);

        Assert.Equal(SettlementStatus.Settled, detail.SettlementStatus);
    }

    /// <summary>Housekeeping commands (line edits, discounts, owner assignment) never carry the verdict.</summary>
    [Fact]
    public async Task HousekeepingCommand_DoesNotDecideTheVerdict()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedPostedInvoiceAsync(fixture);
        await SeedCommandAsync(fixture, CommandTypes.SalesInvoicePost, sequence: 1, SettlementState.Settled);
        await SeedCommandAsync(fixture, CommandTypes.SalesInvoiceLineEdit, sequence: 2,
            SettlementState.Rejected, "INVOICE_LINE_INDEX_STALE");

        var detail = await DetailAsync(fixture);

        Assert.Equal(SettlementStatus.Settled, detail.SettlementStatus);
    }

    private static async Task<Animora.Application.UseCases.Sales.ReadModels.InvoiceDetailReadModel> DetailAsync(
        LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesQueries>();
        var detail = await sales.GetInvoiceAsync(InvoiceId, default);
        Assert.NotNull(detail);
        return detail!;
    }

    /// <summary>Seeds one posted invoice the detail query can project.</summary>
    private static async Task SeedPostedInvoiceAsync(LocalStoreTestFixture fixture)
    {
        var invoice = Invoice.Create(fixture.TenantId, InvoiceId, InvoiceKind.DirectSale, DateTimeOffset.UnixEpoch,
            "U1", ownerId: "OWN1", id: InvoiceId).Value!;
        invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(12_500), 0m, 0m, InvoiceLineSource.Manual, null));
        Assert.True(invoice.Post(fixture.Clock).IsSuccess);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        db.Add(invoice);
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds one settled journal row addressed at the invoice, in the given final state.</summary>
    private static async Task SeedCommandAsync(LocalStoreTestFixture fixture, string commandType, long sequence,
        SettlementState status, string? errorCode = null)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var envelope = CommandEnvelope.Create(IdGenerator.NewId(), fixture.TenantId, "U1", "D1", commandType,
            "Invoice", InvoiceId, "{}", "fp-" + sequence, DateTimeOffset.UnixEpoch, sequence);
        var row = PendingCommand.Create(envelope, fixture.Clock.UtcNow);
        db.Add(row);
        await db.SaveChangesAsync();
        await db.Set<PendingCommand>().Where(p => p.Id == row.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status)
                .SetProperty(p => p.LastErrorCode, errorCode));
    }
}
