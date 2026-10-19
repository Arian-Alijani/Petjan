using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Vaccination;
using Animora.Application.UseCases.Visits;
using Animora.Domain.Clinical;

namespace Animora.LiveTest.Correctness;

/// <summary>Reception walk-ins, appointments (book / reschedule / cancel), visits (encounter) and vaccination.</summary>
public static class ClinicalScenarios
{
    private const string NoDoctor = "DOCTOR_NOT_AVAILABLE";
    private const string OutsideHours = "DOCTOR_OUTSIDE_WORKING_HOURS";

    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        new("Reception", "Walk-in ticket on A reaches B with a ticket number", WalkInAsync),
        new("Appointments", "Book → reschedule → cancel converge on both clients", AppointmentLifecycleAsync),
        new("Appointments", "Double-booking the same doctor slot is refused", DoubleBookingAsync),
        new("Visits", "Start visit → vitals → note → complete converge on both clients", VisitEncounterAsync),
        new("Vaccination", "Register dose → mark next dose done → duplicate refused → reminder audit", VaccinationAsync),
    ];

    private static async Task WalkInAsync(ScenarioContext c)
    {
        var created = await c.AcceptAsync(c.A, new CreateWalkInCommand(c.Name("walkin-pet"), c.Name("walkin-owner"), ScenarioContext.NextPhone(), null, false));
        var entryId = ScenarioContext.DerivedId(created, 0);
        var entry = await c.AwaitConvergedAsync("ReceptionQueueEntry", entryId);
        c.Check(entry.Num("ticketNumber") > 0, "walk-in replicated without a ticket number.");
    }

    /// <summary>Next non-Friday date ≥ 3 days ahead (default working week is Saturday–Thursday).</summary>
    private static DateOnly BookableDate()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        while (d.DayOfWeek == DayOfWeek.Friday) d = d.AddDays(1);
        return d;
    }

    /// <summary>Books a 15-minute slot on a random free time; retries on a slot clash, SKIPs when the doctor cannot take bookings.</summary>
    private static async Task<(Client.SubmitOutcome Outcome, DateOnly Date, int Start)> BookFreeSlotAsync(ScenarioContext c, string doctor, OwnerPet op, string note)
    {
        var date = BookableDate();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var start = 480 + 5 * Random.Shared.Next(0, 120); // 08:00 … 17:55
            var booked = await c.SubmitAsync(c.A, new BookAppointmentCommand(date, start, 15, "CHECKUP", doctor, op.PetId, op.OwnerId, note));
            if (booked.IsAccepted) return (booked, date, start);
            var code = booked.Settlement?.ErrorCode;
            if (code is NoDoctor or OutsideHours) throw new ScenarioSkipped($"doctor cannot take bookings: {code}.");
            if (code != "APPOINTMENT_SLOT_CONFLICT") throw ScenarioContext.Fail($"BookAppointment failed unexpectedly: {booked.Describe()}");
        }

        throw ScenarioContext.Fail("could not find a free doctor slot after 8 attempts.");
    }

    private static async Task AppointmentLifecycleAsync(ScenarioContext c)
    {
        var doctor = await c.Shared.DoctorIdAsync() ?? throw new ScenarioSkipped("tenant has no active doctor (StaffMembership role=Doctor).");
        var op = await c.Shared.OwnerPetAsync();
        var (booked, date, start) = await BookFreeSlotAsync(c, doctor, op, "lt-booking");
        var id = ScenarioContext.IdOf(booked);
        var row = await c.AwaitConvergedAsync("Appointment", id, r => r.Str("petId") == op.PetId, "booked");
        c.Check(row.Str("doctorUserId") == doctor, "appointment replicated with a different doctor.");

        var before = row.Str("startUtc");
        await c.AcceptAsync(c.B, new RescheduleAppointmentCommand(id, date, start + 60 <= 1425 ? start + 60 : start - 60, 15));
        var moved = await c.AwaitConvergedAsync("Appointment", id, r => r.Str("startUtc") != before, "rescheduled");
        c.Check(moved.Num("rescheduleCount") >= 1, "rescheduleCount did not increase.");

        await c.AcceptAsync(c.A, new CancelAppointmentCommand(id, "lt-cancel"));
        var cancelled = await c.AwaitConvergedAsync("Appointment", id, r => r.Str("cancelReason") == "lt-cancel", "cancelled");
        c.Check(cancelled.Num("state") != row.Num("state"), "appointment state did not change on cancel.");
    }

    private static async Task DoubleBookingAsync(ScenarioContext c)
    {
        var doctor = await c.Shared.DoctorIdAsync() ?? throw new ScenarioSkipped("tenant has no active doctor (StaffMembership role=Doctor).");
        var op = await c.Shared.OwnerPetAsync();
        var (booked, date, start) = await BookFreeSlotAsync(c, doctor, op, "lt-first");
        var id = ScenarioContext.IdOf(booked);
        await c.RejectAsync(c.B, new BookAppointmentCommand(date, start, 15, "CHECKUP", doctor, op.PetId, op.OwnerId, "lt-second"), "APPOINTMENT_SLOT_CONFLICT");
        await c.TrySubmitAsync(c.A, new CancelAppointmentCommand(id, "lt-cleanup"));
    }

    private static async Task VisitEncounterAsync(ScenarioContext c)
    {
        var doctor = await c.Shared.DoctorIdAsync() ?? throw new ScenarioSkipped("tenant has no active doctor (StaffMembership role=Doctor).");
        var op = await c.Shared.OwnerPetAsync();
        await c.AcceptOrSkipAsync(c.A, new StartNewVisitCommand(doctor, op.PetName, "dog", op.OwnerName, op.Phone, "CHECKUP", false, "lt visit", op.PetId), NoDoctor);
        var visit = await c.AwaitFindAsync(c.A, "Visit", (_, r) => r.Str("petId") == op.PetId, "started visit");
        var visitId = visit.Id;
        await c.AwaitRowAsync(c.B, "Visit", visitId, what: "started visit");

        await c.AcceptAsync(c.A, new SaveVisitVitalsCommand(visitId, 11.2m, 38.6m, 90, 24));
        await c.AcceptAsync(c.A, new SaveVisitNoteCommand(visitId, VisitNoteField.Diagnosis, "lt diagnosis"));
        var edited = await c.AwaitConvergedAsync("Visit", visitId, r => r.Str("diagnosis") == "lt diagnosis", "diagnosis saved");
        c.Check(edited.Prop("vitals") is { ValueKind: System.Text.Json.JsonValueKind.Object }, "vitals did not replicate with the visit.");

        await c.AcceptAsync(c.A, new CompleteVisitCommand(visitId));
        var done = await c.AwaitConvergedAsync("Visit", visitId, r => r.Str("completedUtc") is not null, "completed");
        c.Check(done.Num("state") != visit.Row.Num("state"), "visit state did not change on completion.");
        await c.RejectAsync(c.B, new SaveVisitNoteCommand(visitId, VisitNoteField.Plan, "lt after completion"));
    }

    private static async Task VaccinationAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var code = "LT" + c.RunId.ToUpperInvariant();
        var registered = await c.AcceptAsync(c.A, new RegisterVaccinationCommand(op.PetId, code, c.Name("vaccine"), 1, DateTimeOffset.UtcNow, 3, "LT-BATCH"));
        var first = ScenarioContext.IdOf(registered);
        await c.AwaitConvergedAsync("VaccinationRecord", first, r => r.Str("vaccineCode") == code, "dose 1");

        var done = await c.AcceptAsync(c.B, new MarkVaccinationDoneCommand(first));
        var second = await c.AwaitFindAsync(c.A, "VaccinationRecord", (id, r) => id != first && r.Str("petId") == op.PetId && r.Str("vaccineCode") == code, "dose 2");
        c.Check(second.Row.Num("doseNumber") == 2, $"next dose replicated as dose {second.Row.Num("doseNumber")}, expected 2.");
        await c.AwaitRowAsync(c.B, "VaccinationRecord", second.Id, what: "dose 2");

        await c.RejectAsync(c.A, new MarkVaccinationDoneCommand(first), "VACCINATION_ALREADY_DONE");
        await c.AcceptAsync(c.A, new RecordVaccinationReminderCommand(first, "lt reminder text (not sent)"));
        await c.AwaitFindAsync(c.B, "MedicalHistoryEntry", (_, r) => r.Str("sourceEntityId") == first, "vaccination history entry");
        c.Log($"mark-done settled as {done.Settlement?.Outcome}");
    }
}
