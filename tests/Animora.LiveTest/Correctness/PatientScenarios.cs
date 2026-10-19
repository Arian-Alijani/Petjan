using Animora.Application.UseCases.Patients;
using Animora.Domain.Patients;

namespace Animora.LiveTest.Correctness;

/// <summary>Owners, pets and weight entries: create → converge, edit → converge, stale-version conflict, delete, validation.</summary>
public static class PatientScenarios
{
    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        new("Patients", "Create owner + pet on A → B converges to the same ids and version", CreateAsync),
        new("Patients", "Edit owner on B → A converges; version moves forward", EditOwnerAsync),
        new("Patients", "Edit pet + record weight → both clients converge", EditPetAndWeightAsync),
        new("Patients", "Stale ExpectedServerVersion on owner edit settles as Conflict; loser resyncs and retries", ConflictAsync),
        new("Patients", "Delete owner (no history) → tombstone reaches B", DeleteAsync),
        new("Patients", "Invalid phone is rejected with a validation error and creates nothing", ValidationAsync),
    ];

    private static async Task CreateAsync(ScenarioContext c)
    {
        var name = c.Name("owner");
        var petName = c.Name("pet");
        var created = await c.AcceptAsync(c.A, new CreateOwnerCommand(name, ScenarioContext.NextPhone(), OwnerGender.Female, null, "1234567890", "lt address",
            petName, PetSpecies.Cat, "persian", "white", PetGender.Female, DateTimeOffset.UtcNow.AddYears(-2), null, null, true));
        var ownerId = ScenarioContext.IdOf(created);
        var owner = await c.AwaitConvergedAsync("Owner", ownerId, r => r.Str("fullName") == name, "with the created name");
        c.Check(owner.Str("phonePrimary")?.Length > 0, "owner replicated without a primary phone.");
        var pet = await c.AwaitFindAsync(c.B, "Pet", (_, r) => r.Str("ownerId") == ownerId && r.Str("name") == petName, "owner's pet");
        c.Check(pet.Row.Num("species") == (long)PetSpecies.Cat, $"pet species replicated as {pet.Row.Num("species")}, expected {(long)PetSpecies.Cat}.");
        c.Check(pet.Row.Prop("isNeutered") is { ValueKind: System.Text.Json.JsonValueKind.True }, "pet 'isNeutered' did not replicate as true.");
        c.AddCleanup("create owner+pet", async () => { await c.TrySubmitAsync(c.A, new DeletePetCommand(pet.Id)); await c.TrySubmitAsync(c.A, new DeleteOwnerCommand(ownerId)); });
    }

    private static async Task EditOwnerAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var before = await c.AwaitConvergedAsync("Owner", op.OwnerId);
        var newName = c.Name("owner-renamed");
        await c.AcceptAsync(c.B, new UpdateOwnerCommand(op.OwnerId, newName, op.Phone, OwnerGender.Male, "09120000001", null, "lt edited address"));
        var after = await c.AwaitConvergedAsync("Owner", op.OwnerId, r => r.Str("fullName") == newName, "renamed");
        c.Check(after.Version != before.Version, $"owner version did not move after an edit (still {after.Version}).");
        c.Check(after.Str("phoneSecondary") == "09120000001", "secondary phone edit did not replicate.");
    }

    private static async Task EditPetAndWeightAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var newPet = c.Name("pet-edited");
        await c.AcceptAsync(c.A, new UpdatePetInfoCommand(op.PetId, newPet, "labrador", "gold", PetGender.Male, null, true, "peanuts", null));
        var pet = await c.AwaitConvergedAsync("Pet", op.PetId, r => r.Str("name") == newPet, "renamed");
        c.Check(pet.Str("breed") == "labrador", "pet breed edit did not replicate.");

        await c.AcceptAsync(c.B, new AddWeightEntryCommand(op.PetId, 12.5m, DateTimeOffset.UtcNow, "lt weight"));
        var weight = await c.AwaitFindAsync(c.A, "WeightEntry", (_, r) => r.Str("petId") == op.PetId, "pet weight");
        c.Check(weight.Row.Prop("weightKg") is { ValueKind: System.Text.Json.JsonValueKind.Number } kg && kg.GetDecimal() == 12.5m,
            $"weight replicated as {weight.Row.Prop("weightKg")}, expected 12.5.");
    }

    private static async Task ConflictAsync(ScenarioContext c)
    {
        var op = await c.Shared.OwnerPetAsync();
        var v1 = (await c.AwaitConvergedAsync("Owner", op.OwnerId)).Version ?? throw ScenarioContext.Fail("owner has no version token.");

        var aName = c.Name("owner-a");
        await c.AcceptAsync(c.A, new UpdateOwnerCommand(op.OwnerId, aName, op.Phone, OwnerGender.Male, null, null, null), expectedServerVersion: v1);

        // B still holds v1: its edit must lose.
        var bName = c.Name("owner-b");
        var stale = new UpdateOwnerCommand(op.OwnerId, bName, op.Phone, OwnerGender.Male, null, null, null);
        await c.ConflictAsync(c.B, stale, v1);

        // Loser resyncs (sees A's edit and the new version), then retries successfully.
        var current = await c.AwaitRowAsync(c.B, "Owner", op.OwnerId, r => r.Str("fullName") == aName, "A's edit");
        await c.AcceptAsync(c.B, stale, expectedServerVersion: current.Version);
        var final = await c.AwaitConvergedAsync("Owner", op.OwnerId, r => r.Str("fullName") == bName, "B's retried edit");
        c.Check(final.Version != current.Version, "retried edit did not advance the version.");
    }

    private static async Task DeleteAsync(ScenarioContext c)
    {
        var created = await c.AcceptAsync(c.A, new CreateOwnerCommand(c.Name("owner-del"), ScenarioContext.NextPhone(), OwnerGender.Unspecified,
            null, null, null, null, null, null, null, null, null, null, null, false));
        var id = ScenarioContext.IdOf(created);
        await c.AwaitConvergedAsync("Owner", id);
        await c.AcceptAsync(c.A, new DeleteOwnerCommand(id));
        await c.AwaitGoneAsync(c.B, "Owner", id);
        await c.AwaitGoneAsync(c.A, "Owner", id);
    }

    private static async Task ValidationAsync(ScenarioContext c)
    {
        var name = c.Name("owner-bad");
        await c.RejectAsync(c.A, new CreateOwnerCommand(name, "---", OwnerGender.Unspecified, null, null, null,
            null, null, null, null, null, null, null, null, false));
        await c.PullAsync(c.B);
        c.Check(c.B.Replica.Find("Owner", (_, r) => r.Str("fullName") == name) is null, "a rejected owner command left a row behind.");
    }
}
