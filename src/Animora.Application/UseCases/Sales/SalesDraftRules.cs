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

internal static class SalesDraftRules
{
    public static ValueTask<IReadOnlyList<ValidationFailure>> Check(params ValidationFailure?[] checks) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(checks.OfType<ValidationFailure>().ToArray());

    public static Result<Unit> Outcome(Result result) => result.IsSuccess
        ? Result<Unit>.Ok(Unit.Value) : Result<Unit>.Fail(result.Error, result.Detail);

    public static async Task<Result<Invoice>> Find(IInvoiceRepository invoices, string id, CancellationToken ct)
    {
        var invoice = await invoices.FindAsync(id, ct);
        return invoice is null || invoice.Deleted
            ? Result<Invoice>.Fail(ErrorCodes.InvoiceNotFound)
            : Result<Invoice>.Ok(invoice);
    }

    public static Result<InvoiceLine> LineAt(Invoice invoice, int index, string expectedLineId)
    {
        if (invoice.State != InvoiceState.Draft) return Result<InvoiceLine>.Fail(ErrorCodes.InvoiceImmutable);
        if (index < 0 || index >= invoice.Lines.Count || invoice.Lines[index].LineId != expectedLineId)
            return Result<InvoiceLine>.Fail(ErrorCodes.InvoiceLineIndexStale);
        return Result<InvoiceLine>.Ok(invoice.Lines[index]);
    }
}

