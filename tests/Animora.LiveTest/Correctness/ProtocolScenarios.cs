using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Purchasing;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;

namespace Animora.LiveTest.Correctness;

/// <summary>
/// Transport-level guarantees every feature relies on: idempotent retry, fingerprint integrity, batch ordering,
/// offline queue replay with dependencies, operation-id → server-id map, and snapshot + incremental feed convergence.
/// Uses suppliers / owners as cheap probe aggregates.
/// </summary>
public static class ProtocolScenarios
{
    public static IReadOnlyList<ScenarioDef> All { get; } =
    [
        new("Protocol", "Idempotent retry: same envelope twice → same result, one row", IdempotentRetryAsync),
        new("Protocol", "Fingerprint integrity: same operation id + different payload is refused", FingerprintMismatchAsync),
        new("Protocol", "Offline queue: 5 queued commands replay in one batch, order kept, replay adds nothing", OfflineQueueAsync),
        new("Protocol", "Offline queue: dependent commands (owner → pet) settle in order", DependentChainAsync),
        new("Protocol", "Operation-id → server-id map matches the settlement", IdMapAsync),
        new("Protocol", "Feed: fresh snapshot client and incremental client converge", SnapshotAndIncrementalAsync),
    ];

    private static async Task IdempotentRetryAsync(ScenarioContext c)
    {
        var name = c.Name("idem-supplier");
        var envelope = c.A.BuildEnvelope(new CreateSupplierCommand(name, null, 0, ScenarioContext.NextPhone()));
        var first = await c.A.SubmitWithRetryAsync(envelope, ct: c.Ct);
        c.Log($"A first submit → {first.Describe()}");
        c.Check(first.IsAccepted, $"first submit not accepted: {first.Describe()}");

        var second = await c.A.SubmitWithRetryAsync(envelope, ct: c.Ct);
        c.Log($"A identical resubmit → {second.Describe()}");
        c.Check(second.IsAccepted, $"identical resubmit not accepted (must return the stored result): {second.Describe()}");
        c.Check(second.ServerEntityId == first.ServerEntityId,
            $"resubmit produced a different entity ({first.ServerEntityId} vs {second.ServerEntityId}) — idempotency broken.");

        var row = await c.AwaitRowAsync(c.B, "Supplier", ScenarioContext.IdOf(first), what: "supplier");
        c.Check(c.B.Replica.Rows("Supplier").Count(r => r.Value.Str("name") == name) == 1,
            $"expected exactly one supplier named '{name}' after a resubmit, found {c.B.Replica.Rows("Supplier").Count(r => r.Value.Str("name") == name)}.");
        c.Check(row.Version is not null, "supplier replicated without a version token.");
    }

    private static async Task FingerprintMismatchAsync(ScenarioContext c)
    {
        var op = IdGenerator.NewId();
        var original = c.A.BuildEnvelope(new CreateSupplierCommand(c.Name("fp-a"), null, 0, null), operationId: op);
        var accepted = await c.A.SubmitWithRetryAsync(original, ct: c.Ct);
        c.Check(accepted.IsAccepted, $"original not accepted: {accepted.Describe()}");

        var tampered = c.A.BuildEnvelope(new CreateSupplierCommand(c.Name("fp-b"), null, 0, null), operationId: op);
        var outcome = await c.A.SubmitAsync(tampered, c.Ct);
        c.Log($"A same op id, different payload → {outcome.Describe()}");
        c.Check(!outcome.IsAccepted, "a different payload under an already-used operation id was ACCEPTED (replay-tamper hole).");
        c.Check(outcome.Settlement?.ErrorCode == "IDEMPOTENCY_FINGERPRINT_MISMATCH" || outcome.TransportError is not null || outcome.IsRejected,
            $"expected IDEMPOTENCY_FINGERPRINT_MISMATCH, got: {outcome.Describe()}");
    }

    private static async Task OfflineQueueAsync(ScenarioContext c)
    {
        var names = Enumerable.Range(0, 5).Select(i => c.Name($"queued-{i}")).ToArray();
        // "Offline": envelopes are built (journaled) but nothing is sent yet.
        var queue = names.Select(n => c.A.BuildEnvelope(new CreateSupplierCommand(n, null, 0, null))).ToList();
        c.Log($"A queued {queue.Count} commands offline");

        var (results, error, elapsed) = await c.A.SubmitBatchAsync(queue, c.Ct);
        c.Check(results is not null, $"batch transport failed: {error}");
        c.Log($"A batch of {queue.Count} settled in {elapsed.TotalMilliseconds:0} ms");
        c.Check(results!.Count == queue.Count, $"batch returned {results.Count} results for {queue.Count} commands.");
        for (var i = 0; i < queue.Count; i++)
        {
            c.Check(results[i].OperationId == queue[i].OperationId, $"batch result {i} is for {results[i].OperationId}, expected {queue[i].OperationId} — order not preserved.");
            c.Check(results[i].Outcome == SettlementOutcome.Accepted, $"batch item {i} settled {results[i].Outcome} [{results[i].ErrorCode}] {results[i].Detail}");
        }

        // Replay the identical batch (a crash-after-send retry): must settle identically and create nothing new.
        var (replay, replayError, _) = await c.A.SubmitBatchAsync(queue, c.Ct);
        c.Check(replay is not null, $"batch replay transport failed: {replayError}");
        for (var i = 0; i < queue.Count; i++)
            c.Check(replay![i].Outcome == SettlementOutcome.Accepted && replay[i].ServerEntityId == results[i].ServerEntityId,
                $"replayed item {i} differs: {replay[i].Outcome} entity {replay[i].ServerEntityId} (first {results[i].ServerEntityId}).");

        foreach (var (envelope, i) in queue.Select((e, i) => (e, i)))
            await c.AwaitRowAsync(c.B, "Supplier", results[i].ServerEntityId ?? envelope.OperationId, what: $"queued supplier {i}");
        foreach (var n in names)
            c.Check(c.B.Replica.Rows("Supplier").Count(r => r.Value.Str("name") == n) == 1, $"supplier '{n}' duplicated or missing on B after batch replay.");
    }

    private static async Task DependentChainAsync(ScenarioContext c)
    {
        var ownerOp = IdGenerator.NewId();
        var ownerName = c.Name("chain-owner");
        var owner = c.A.BuildEnvelope(new CreateOwnerCommand(ownerName, ScenarioContext.NextPhone(), OwnerGender.Female, null, null, null,
            null, null, null, null, null, null, null, null, false), operationId: ownerOp);
        // The pet references the owner by its client-minted id (slot 0 of the owner operation), exactly like an offline desktop.
        var petName = c.Name("chain-pet");
        var pet = c.A.BuildEnvelope(new CreatePetCommand(ownerOp, petName, PetSpecies.Cat, null, null, PetGender.Female, null, null, null, false),
            dependsOnOperationId: ownerOp);

        var (results, error, _) = await c.A.SubmitBatchAsync([owner, pet], c.Ct);
        c.Check(results is { Count: 2 }, $"dependent batch failed: {error}");
        c.Check(results![0].Outcome == SettlementOutcome.Accepted, $"owner settled {results[0].Outcome} [{results[0].ErrorCode}] {results[0].Detail}");
        c.Check(results[1].Outcome == SettlementOutcome.Accepted, $"dependent pet settled {results[1].Outcome} [{results[1].ErrorCode}] {results[1].Detail}");

        var ownerId = results[0].ServerEntityId ?? ownerOp;
        await c.AwaitRowAsync(c.B, "Owner", ownerId, what: "chain owner");
        var found = await c.AwaitFindAsync(c.B, "Pet", (_, r) => r.Str("ownerId") == ownerId && r.Str("name") == petName, "dependent pet");
        c.Check(found.Row.Str("ownerId") == ownerId, "pet is not attached to the owner created in the same queue.");
        c.AddCleanup("chain pet", async () => { await c.TrySubmitAsync(c.A, new DeletePetCommand(found.Id)); await c.TrySubmitAsync(c.A, new DeleteOwnerCommand(ownerId)); });
    }

    private static async Task IdMapAsync(ScenarioContext c)
    {
        var created = await c.AcceptAsync(c.A, new CreateSupplierCommand(c.Name("idmap"), null, 0, null));
        var map = await c.GetIdMapAsync(c.A, created.Envelope.OperationId);
        c.Log($"id map has {map.Count} entries");
        c.Check(map.Count > 0, "no id-map entries for a settled create command.");
        c.Check(map.Any(m => m.ServerId == ScenarioContext.IdOf(created)),
            $"id map {string.Join(",", map.Select(m => m.ServerId))} does not contain the settled entity id {ScenarioContext.IdOf(created)}.");
    }

    private static async Task SnapshotAndIncrementalAsync(ScenarioContext c)
    {
        var created = await c.AcceptAsync(c.A, new CreateSupplierCommand(c.Name("feed"), null, 0, null));
        var id = ScenarioContext.IdOf(created);
        await c.AwaitConvergedAsync("Supplier", id, what: "supplier");

        // A brand-new device performs a full snapshot and must reach the same state as the incremental clients.
        await using var fresh = Client.VirtualClient.Create(c.Options, "snap", slot: 2);
        var login = await fresh.LoginAsync(c.Ct);
        c.Check(login is null, $"third device login failed: {login}");
        await c.PullAsync(fresh);
        c.Check(fresh.Replica.HasSnapshot, "fresh device did not apply a snapshot.");
        c.Check(fresh.Replica.TryGet("Supplier", id, out var snap), "snapshot is missing a supplier that incremental clients hold.");
        c.Check(c.B.Replica.TryGet("Supplier", id, out var inc) && inc.Version == snap.Version, "snapshot and incremental versions differ.");
        c.Check(fresh.Replica.Count("Supplier") == c.B.Replica.Count("Supplier"),
            $"snapshot holds {fresh.Replica.Count("Supplier")} suppliers, incremental client B holds {c.B.Replica.Count("Supplier")}.");
    }
}
