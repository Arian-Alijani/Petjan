using Animora.Domain.Boarding;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Server.Tests.HandlersS07;
using Xunit;
namespace Animora.Server.Tests.HandlersS08;

public sealed class ServerS08RederiveTests
{
    [Fact]
    public void BoardingTotals_AreRecomputedFromRatesAndConsumedItems()
    {
        using var f = new SchedulingBoardingHandlerFixture();
        var total = StayChargeCalculator.Accrued(f.Stay, [], f.Clinical.Intake.Clock.UtcNow, TimeZoneInfo.Utc);
        Assert.Equal(210, total.Minor); // 2 nights × 100 + one item × 10, not a client-supplied total.
        Assert.Equal(ErrorCodes.StayInvoiceRequired, f.Stay.CompleteCheckout(f.Clinical.Intake.Clock).Error);
    }
}
public sealed class SlotConflictServerTests
{
    [Fact]
    public void Overlap_Rejected_Adjacent_Allowed()
    {
        var start = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
        ExistingSlot[] busy = [new("existing", "doctor", new AppointmentSlot(start, 30), AppointmentState.Scheduled)];
        Assert.Equal(ErrorCodes.AppointmentSlotConflict, SlotConflictPolicy.EnsureFree(new AppointmentSlot(start.AddMinutes(15), 30), "doctor", busy).Error);
        Assert.True(SlotConflictPolicy.EnsureFree(new AppointmentSlot(start.AddMinutes(30), 30), "doctor", busy).IsSuccess);
    }
}
