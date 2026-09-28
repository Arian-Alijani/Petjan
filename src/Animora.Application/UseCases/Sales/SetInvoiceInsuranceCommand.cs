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
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceInsuranceSet)]
public sealed record SetInvoiceInsuranceCommand(string InvoiceId, string? InsurerId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceInsuranceSet;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}
public sealed class SetInvoiceInsuranceCommandValidator : IValidator<SetInvoiceInsuranceCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(SetInvoiceInsuranceCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)));
}
public sealed class SetInvoiceInsuranceCommandHandler(IInvoiceRepository invoices, ISalesDraftData data)
    : ICommandHandler<SetInvoiceInsuranceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SetInvoiceInsuranceCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var invoice = found.Value!;
        if (c.InsurerId is null) return SalesDraftRules.Outcome(invoice.SetInsurance(null, 0));
        var insurer = await data.FindInsurerAsync(c.InsurerId, ct);
        if (insurer is null) return Result<Unit>.Fail(ErrorCodes.InsurerNotFound);
        // Effective percentage is rounded down so Money.ApplyPercent cannot exceed the ceiling.
        var beforeCoverage = checked(invoice.GrandTotal.Minor + invoice.InsuranceCoverage.Minor);
        var effective = beforeCoverage <= 0 ? 0m : Math.Min(insurer.CoveragePercent,
            Math.Max(0, insurer.CeilingMinor) * 100m / beforeCoverage);
        effective = Math.Floor(effective * 1_000_000m) / 1_000_000m;
        while (effective > 0 && Money.Irt(beforeCoverage).ApplyPercent(effective).Minor > insurer.CeilingMinor)
            effective -= 0.000001m;
        return SalesDraftRules.Outcome(invoice.SetInsurance(insurer.Id, effective));
    }
}

