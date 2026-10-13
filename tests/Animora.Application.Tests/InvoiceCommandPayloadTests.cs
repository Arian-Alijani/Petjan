using System.Text.Json;
using Animora.Application.Commands;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// T2 (client/server coherence): an invoice reaches the server as a chain of small commands (create → lines → owner → discount →
/// insurance → post). Each payload must be set-equal to its record's constructor parameters (server shape gate) and the ids the
/// chain addresses must be <b>deterministic</b> (derived from the creating command's OperationId) so the server — which re-runs the
/// same handler — materialises the same invoice/line ids instead of an empty or mismatched invoice.
/// </summary>
public sealed class InvoiceCommandPayloadTests
{
    private static readonly string[] JournalingKeys = ["commandTypeName", "aggregateTypeName", "aggregateIdOrNull"];

    private static HashSet<string> Keys<T>(T command)
    {
        using var doc = JsonDocument.Parse(CommandFingerprint.CanonicalJson(command!));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> CtorKeys(Type type) =>
        type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!)).ToHashSet(StringComparer.Ordinal);

    private static void AssertShape<T>(T command)
    {
        var keys = Keys(command);
        foreach (var journaling in JournalingKeys) Assert.DoesNotContain(journaling, keys);
        Assert.Equal(CtorKeys(typeof(T)), keys);
    }

    [Fact]
    public void EveryInvoiceCommand_Payload_IsSetEqualToConstructorParameters()
    {
        AssertShape(new CreateBlankInvoiceCommand("OWN1", null, "PET1"));
        AssertShape(new CreateInvoiceFromVisitCommand("H1"));
        AssertShape(new CreateInvoiceFromStayCommand("H1"));
        AssertShape(new AddInvoiceLineCommand("INV1", "CAT1", 2m));
        AssertShape(new AddCustomInvoiceLineCommand("INV1", "Custom", 1000, 1m));
        AssertShape(new AssignInvoiceOwnerCommand("INV1", "OWN1", "PET1", null));
        AssertShape(new SetInvoiceDiscountCommand("INV1", DiscountKind.Percent, 10m));
        AssertShape(new SetInvoiceInsuranceCommand("INV1", "INS1"));
        AssertShape(new EditInvoiceLineCommand("INV1", 0, "L1", 2m, 0m));
        AssertShape(new RemoveInvoiceLineCommand("INV1", 0, "L1"));
        AssertShape(new PostInvoiceCommand("INV1"));
    }

    [Fact]
    public void LineAddCommands_HaveDistinctPayloadShapes_SoServerResolvesEachUniquely()
    {
        // Both map to sales.invoice.line.add; the server tells them apart purely by key set.
        Assert.NotEqual(Keys(new AddInvoiceLineCommand("I", "C", 1m)), Keys(new AddCustomInvoiceLineCommand("I", "T", 1, 1m)));
    }

    [Fact]
    public async Task CreateBlankInvoice_UsesOperationIdAsInvoiceId_SoClientAndServerAgree()
    {
        var invoices = new SalesInvoiceRepository();
        var handler = new CreateBlankInvoiceCommandHandler(invoices, SalesTestData.Clock);

        var result = await handler.HandleAsync(new CreateBlankInvoiceCommand("OWN1", null), SalesTestData.Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(SalesTestData.Context.OperationId, result.Value);
        Assert.NotNull(await invoices.FindAsync(SalesTestData.Context.OperationId));
    }

    [Fact]
    public async Task AddCustomLine_UsesOperationIdAsLineId_AndLandsOnTheInvoice()
    {
        var invoice = SalesTestData.Draft();
        var handler = new AddCustomInvoiceLineCommandHandler(new SalesInvoiceRepository(invoice), SalesTestData.Clock);
        var before = invoice.Lines.Count;

        var result = await handler.HandleAsync(new AddCustomInvoiceLineCommand(invoice.Id, "Extra", 500, 1m), SalesTestData.Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(before + 1, invoice.Lines.Count);
        Assert.Equal(SalesTestData.Context.OperationId, invoice.Lines[invoice.Lines.Count - 1].LineId);
    }
}
