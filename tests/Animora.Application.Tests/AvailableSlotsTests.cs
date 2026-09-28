using Animora.Application.UseCases.Scheduling;
using Animora.Domain.Scheduling;
using Xunit;

namespace Animora.Application.Tests;

public sealed class AvailableSlotsTests
{
    [Fact]
    public async Task GridStepsByFifteen_ExcludesBusy_AndNeverOverhangsEnd()
    {
        var day = new DateOnly(2026, 3, 2);
        var queries = new BookingQueriesFake { Hours = (480, 600, 127) };
        queries.Busy.Add(new ExistingSlot("busy", "doctor", new AppointmentSlot(
            new DateTimeOffset(2026, 3, 2, 8, 30, 0, TimeSpan.Zero), 30), AppointmentState.Confirmed));
        var handler = new AvailableSlotsQueryHandler(queries, new TestZone());
        var result = await handler.HandleAsync(new AvailableSlotsQuery("doctor", day, 45),
            TestHost.QueryCtx(), default);
        Assert.True(result.IsSuccess);
        var slots = result.Value!;
        Assert.All(slots, slot => Assert.True(slot.Minutes + 45 <= 600));
        Assert.Equal(15, slots[1].Minutes - slots[0].Minutes);
        Assert.False(slots.Single(s => s.Minutes == 8 * 60).IsFree);
        Assert.False(slots.Single(s => s.Minutes == 8 * 60 + 30).IsFree);
        Assert.True(slots.Single(s => s.Minutes == 9 * 60).IsFree);
    }
}
