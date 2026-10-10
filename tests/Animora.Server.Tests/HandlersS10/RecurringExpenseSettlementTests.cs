using Animora.Application.UseCases.Finance;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Xunit;
namespace Animora.Server.Tests.HandlersS10;
public sealed class RecurringExpenseSettlementTests
{
    [Fact] public async Task Pay_IsIdempotent_PerTemplateAndPeriod()
    {
        using var f = new MoneyStockHandlerFixture();
        var service = f.Clinical.Intake.CreateService(f.Catalog, f.Clinical.Dispatcher);
        var command = f.Command(typeof(PayRecurringExpenseForPeriodCommand), false);
        var envelope = f.Clinical.Envelope(command);
        var accepted = await service.SubmitAsync(envelope);
        Assert.Equal(SettlementOutcome.Accepted, accepted.Value!.Outcome);
        Assert.Equal(accepted.Value, (await service.SubmitAsync(envelope)).Value);
        var duplicate = await service.SubmitAsync(f.Clinical.Envelope(command));
        Assert.Equal(ErrorCodes.RecurringExpenseAlreadyPaid.Code, duplicate.Value!.ErrorCode);
        Assert.Single(f.Recurring.Payments);
    }
}
