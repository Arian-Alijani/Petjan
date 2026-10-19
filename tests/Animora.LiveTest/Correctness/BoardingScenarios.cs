using Animora.Application.UseCases.Boarding;
using Animora.Domain.Boarding;
using Animora.Domain.Patients;

namespace Animora.LiveTest.Correctness;

/// <summary>Boarding rooms and stays: room create/edit, check-in, capacity and availability rules, reschedule, check-out.</summary>
public static class BoardingScenarios
{
    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        new("Boarding", "Create + edit room → B converges (status, capacity)", RoomAsync),
        new("Boarding", "Check-in → reschedule check-out → check-out converge; capacity + maintenance rules enforced", StayLifecycleAsync),
        new("Boarding", "Invalid stay dates are rejected", InvalidDatesAsync),
    ];

    private static async Task<string> NewRoomAsync(ScenarioContext c, int capacity = 1)
    {
        var created = await c.AcceptAsync(c.A, new CreateRoomCommand(c.Code(), c.Name("room"), RoomType.Standard, capacity, "lt room"));
        var id = ScenarioContext.IdOf(created);
        await c.AwaitConvergedAsync("Room", id);
        return id;
    }

    private static async Task RoomAsync(ScenarioContext c)
    {
        var id = await NewRoomAsync(c);
        var name = c.Name("room-renamed");
        await c.AcceptAsync(c.B, new UpdateRoomCommand(id, name, 3, RoomStatus.Available, "lt edited"));
        var row = await c.AwaitConvergedAsync("Room", id, r => r.Str("displayName") == name, "renamed");
        c.Check(row.Num("capacity") == 3, $"room capacity replicated as {row.Num("capacity")}, expected 3.");
        await c.RejectAsync(c.A, new CreateRoomCommand(row.Str("code") ?? "", c.Name("room-dup"), RoomType.Standard, 1, null), "ROOM_CODE_DUPLICATE");
    }

    private static async Task StayLifecycleAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var roomId = await NewRoomAsync(c, capacity: 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var stay = await c.AcceptAsync(c.A, new CreateBoardingStayCommand(op.PetId, roomId, today, today.AddDays(2), 150_000, "lt care"));
        var stayId = ScenarioContext.IdOf(stay);
        var row = await c.AwaitConvergedAsync("BoardingStay", stayId, r => r.Str("roomId") == roomId, "checked in");
        c.Check(row.Str("petId") == op.PetId && row.Str("ownerId") == op.OwnerId, "stay replicated with the wrong pet/owner.");

        // Capacity 1: a second boarder (walk-in) must be refused.
        await c.RejectAsync(c.B, new CreateBoardingStayCommand("", roomId, today, today.AddDays(1), 100_000, null,
            WalkIn: new WalkInBoarder(c.Name("walkin-boarder"), PetSpecies.Cat, null, c.Name("walkin-owner"), ScenarioContext.NextPhone())), "ROOM_AT_CAPACITY");

        var plannedBefore = row.Str("plannedCheckOutUtc");
        await c.AcceptAsync(c.B, new RescheduleStayCheckoutCommand(stayId, today.AddDays(3)));
        await c.AwaitConvergedAsync("BoardingStay", stayId, r => r.Str("plannedCheckOutUtc") != plannedBefore, "check-out rescheduled");

        await c.AcceptAsync(c.A, new CheckoutStayCommand(stayId));
        var done = await c.AwaitConvergedAsync("BoardingStay", stayId, r => r.Str("actualCheckOutUtc") is not null, "checked out");
        c.Check(done.Num("state") != row.Num("state"), "stay state did not change on check-out.");

        // A room under maintenance cannot take a stay.
        await c.AcceptAsync(c.A, new UpdateRoomCommand(roomId, c.Name("room-maint"), 1, RoomStatus.Maintenance, null));
        await c.AwaitConvergedAsync("Room", roomId, r => r.Num("status") == (long)RoomStatus.Maintenance, "in maintenance");
        await c.RejectAsync(c.B, new CreateBoardingStayCommand(op.PetId, roomId, today, today.AddDays(1), 100_000, null), "ROOM_UNAVAILABLE");
    }

    private static async Task InvalidDatesAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var roomId = await NewRoomAsync(c);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await c.RejectAsync(c.A, new CreateBoardingStayCommand(op.PetId, roomId, today, today, 100_000, null), "STAY_DATES_INVALID");
    }
}
