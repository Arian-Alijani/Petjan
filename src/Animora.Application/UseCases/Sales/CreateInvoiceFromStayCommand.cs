using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Sales;

[RequiresPermission(PermissionCodes.SalesDraft)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceCreate)]
public sealed record CreateInvoiceFromStayCommand(string HandoffId) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceCreate;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => null;
}
public sealed class CreateInvoiceFromStayCommandValidator : IValidator<CreateInvoiceFromStayCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateInvoiceFromStayCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.HandoffId, nameof(c.HandoffId)));
}
public sealed class CreateInvoiceFromStayCommandHandler(ISalesDraftData data, IInvoiceRepository invoices, IClock clock)
    : ICommandHandler<CreateInvoiceFromStayCommand, string>
{
    public Task<Result<string>> HandleAsync(CreateInvoiceFromStayCommand c, CommandContext context, CancellationToken ct) =>
        SalesHandoffFactory.CreateAsync(data, invoices, clock, "Boarding", c.HandoffId, context, ct);
}

internal static class SalesHandoffFactory
{
    public static async Task<Result<string>> CreateAsync(ISalesDraftData data, IInvoiceRepository invoices,
        IClock clock, string source, string handoffId, CommandContext context, CancellationToken ct)
    {
        var handoff = await data.FindHandoffAsync(source, handoffId, ct);
        if (handoff is null) return Result<string>.Fail(ErrorCodes.NotFound);
        if (handoff.AlreadyInvoiced) return Result<string>.Fail(ErrorCodes.HandoffAlreadyInvoiced);
        if (!handoff.IsReady) return Result<string>.Fail(ErrorCodes.ValidationFailed);
        var created = Invoice.Create(context.TenantId, "draft-" + context.OperationId,
            source == "Visit" ? InvoiceKind.Visit : InvoiceKind.Boarding, clock.UtcNow,
            context.UserId, handoff.OwnerId, petId: handoff.PetId, operationId: context.OperationId);
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);
        var invoice = created.Value!;
        var attached = source == "Visit" ? invoice.AttachVisit(handoff.SourceId) : invoice.AttachStay(handoff.SourceId);
        if (attached.IsFailure) return Result<string>.Fail(attached.Error, attached.Detail);

        using var document = JsonDocument.Parse(handoff.LinesJson);
        var root = document.RootElement;
        if (source == "Visit" && root.TryGetProperty("ServiceLines", out var services))
        {
            foreach (var item in services.EnumerateArray())
            {
                var catalogId = item.TryGetProperty("CatalogItemId", out var id) && id.ValueKind == JsonValueKind.String
                    ? id.GetString() : null;
                var catalog = catalogId is null ? null : await data.FindCatalogEntryAsync(catalogId, ct);
                var quantity = item.GetProperty("Quantity");
                var unit = quantity.GetProperty("UnitCode").GetString() ?? "PCS";
                var price = item.GetProperty("UnitPrice").GetProperty("Minor").GetInt64();
                var line = new InvoiceLine(item.GetProperty("LineId").GetString()!,
                    catalog?.Kind ?? InvoiceLineKind.Custom, catalogId, catalog?.ProductId, catalog?.VariantId,
                    item.GetProperty("Title").GetString()!, new Quantity(quantity.GetProperty("Value").GetDecimal(), unit),
                    Money.Irt(price), 0, 0, InvoiceLineSource.Visit,
                    item.GetProperty("LineId").GetString());
                var added = invoice.AddLine(line);
                if (added.IsFailure) return Result<string>.Fail(added.Error, added.Detail);
            }
        }
        else if (source == "Boarding")
        {
            long itemsTotal = 0;
            if (root.TryGetProperty("Items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var catalogId = item.GetProperty("CatalogItemId").GetString();
                    var catalog = catalogId is null ? null : await data.FindCatalogEntryAsync(catalogId, ct);
                    var count = item.GetProperty("Quantity").GetDecimal();
                    var price = item.GetProperty("UnitPriceMinor").GetInt64();
                    itemsTotal += Money.Irt(price).ApplyPercent(count * 100m).Minor;
                    var line = new InvoiceLine(item.GetProperty("ItemId").GetString()!,
                        catalog?.Kind ?? InvoiceLineKind.Boarding, catalogId, catalog?.ProductId, catalog?.VariantId,
                        item.GetProperty("Title").GetString()!, new Quantity(count, catalog?.UnitCode ?? "PCS"),
                        Money.Irt(price), 0, 0, InvoiceLineSource.Stay,
                        item.GetProperty("ItemId").GetString());
                    var added = invoice.AddLine(line);
                    if (added.IsFailure) return Result<string>.Fail(added.Error, added.Detail);
                }
            }
            // A prepaid stay already collected its room in advance; its final (items-only) invoice must
            // carry no room line. The flag is authoritative — never re-derive a room charge for it.
            var roomPrepaid = root.TryGetProperty("RoomPrepaid", out var prepaid) &&
                prepaid.ValueKind == JsonValueKind.True;
            var roomCharge = roomPrepaid ? 0 : root.GetProperty("TotalMinor").GetInt64() - itemsTotal;
            if (roomCharge > 0)
            {
                var room = new InvoiceLine(IdGenerator.NewId(clock.UtcNow), InvoiceLineKind.Boarding,
                    null, null, null, "هزینه اقامت (پانسیون)", new Quantity(1, "PCS"), Money.Irt(roomCharge),
                    0, 0, InvoiceLineSource.Stay, handoff.SourceId);
                var added = invoice.AddLine(room);
                if (added.IsFailure) return Result<string>.Fail(added.Error, added.Detail);
            }
        }
        invoices.Add(invoice);
        return Result<string>.Ok(invoice.Id);
    }
}

