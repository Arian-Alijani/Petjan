using System.Globalization;
using System.Text.Json;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Primitives;
using Animora.LiveTest.Client;
using Animora.LiveTest.Config;

namespace Animora.LiveTest.Correctness;

/// <summary>An assertion or protocol expectation that did not hold. Message is the full English explanation.</summary>
public sealed class ScenarioFailure(string message) : Exception(message);

/// <summary>Thrown by a scenario whose precondition is missing on the live tenant (reported as SKIP, never as PASS).</summary>
public sealed class ScenarioSkipped(string reason) : Exception(reason);

/// <summary>
/// Shared state for one correctness run: two logged-in virtual desktops (A and B), unique-name helpers, the
/// submit / pull / await-convergence primitives every scenario is written with, a step log for failure reports and
/// a best-effort cleanup stack. One instance spans the whole suite so fixtures (owner, pet, product) are created once.
/// </summary>
public sealed class ScenarioContext
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<string> _steps = [];
    private readonly Stack<(string Name, Func<Task> Action)> _cleanup = new();
    private int _counter;

    public ScenarioContext(LiveTestOptions options, string runId, VirtualClient a, VirtualClient b, CancellationToken ct)
    {
        Options = options;
        RunId = runId;
        A = a;
        B = b;
        Ct = ct;
        Shared = new Fixtures(this);
    }

    public LiveTestOptions Options { get; }

    /// <summary>Short unique id of this run; prefixes every created entity name (<c>lt-&lt;run&gt;-…</c>).</summary>
    public string RunId { get; }

    public VirtualClient A { get; }

    public VirtualClient B { get; }

    public CancellationToken Ct { get; }

    /// <summary>How long a client may take to see a settled write in the state feed before it counts as a desync.</summary>
    public TimeSpan ConvergenceTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public Fixtures Shared { get; }

    /// <summary>Steps logged by the scenario currently running (copied into the report on failure).</summary>
    public IReadOnlyList<string> Steps => _steps;

    internal void BeginScenario() => _steps.Clear();

    public void Log(string step)
    {
        _steps.Add(step);
        Console.WriteLine($"      · {step}");
    }

    // ───────────────────────────── naming helpers ─────────────────────────────

    /// <summary>Unique, namespaced display name: <c>lt-&lt;run&gt;-&lt;kind&gt;-&lt;n&gt;</c>.</summary>
    public string Name(string kind) => $"lt-{RunId}-{kind}-{Interlocked.Increment(ref _counter)}";

    /// <summary>Short code for fields with a tight length limit (rooms, cheque numbers): <c>LT&lt;run&gt;&lt;n&gt;</c>.</summary>
    public string Code() => $"LT{RunId.ToUpperInvariant()}{Interlocked.Increment(ref _counter)}";

    /// <summary>Unique mobile number (<c>09</c> + 9 digits) so owners never collide by phone.</summary>
    public static string NextPhone() => "09" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);

    /// <summary>Id of the n-th entity a create command derives from its operation id (slot 0 = the operation id itself).</summary>
    public static string DerivedId(SubmitOutcome created, int slot) =>
        IdGenerator.ForOperation(created.Envelope.OperationId, slot, DateTimeOffset.UtcNow);

    /// <summary>Server id of the aggregate a settled create command produced.</summary>
    public static string IdOf(SubmitOutcome created) => created.ServerEntityId ?? created.Envelope.OperationId;

    // ───────────────────────────── assertions ─────────────────────────────

    public static ScenarioFailure Fail(string message) => new(message);

    public void Check(bool condition, string message)
    {
        if (!condition) throw Fail(message);
    }

    // ───────────────────────────── submit primitives ─────────────────────────────

    /// <summary>Builds a real envelope and submits it (retrying <c>Deferred</c> with the same operation id). Never asserts.</summary>
    public async Task<SubmitOutcome> SubmitAsync<TCommand>(VirtualClient who, TCommand command, string? expectedServerVersion = null,
        string? dependsOn = null)
        where TCommand : IJournaledCommand
    {
        var envelope = who.BuildEnvelope(command, dependsOnOperationId: dependsOn, expectedServerVersion: expectedServerVersion);
        var outcome = await who.SubmitWithRetryAsync(envelope, ct: Ct).ConfigureAwait(false);
        Log($"{who.Label} {command.CommandTypeName} → {Brief(outcome)} ({outcome.Elapsed.TotalMilliseconds:0} ms)");
        return outcome;
    }

    /// <summary>Submits and requires <c>Accepted</c>; otherwise fails with the full settlement description.</summary>
    public async Task<SubmitOutcome> AcceptAsync<TCommand>(VirtualClient who, TCommand command, string? expectedServerVersion = null,
        string? dependsOn = null)
        where TCommand : IJournaledCommand
    {
        var outcome = await SubmitAsync(who, command, expectedServerVersion, dependsOn).ConfigureAwait(false);
        if (!outcome.IsAccepted) throw Fail($"{command.CommandTypeName} was expected to be Accepted but: {outcome.Describe()}");
        return outcome;
    }

    /// <summary>
    /// Like <see cref="AcceptAsync{TCommand}"/>, but a rejection carrying one of <paramref name="skipCodes"/> means the live tenant
    /// lacks a precondition (e.g. no active doctor) — the scenario is reported SKIP with the reason, never PASS.
    /// </summary>
    public async Task<SubmitOutcome> AcceptOrSkipAsync<TCommand>(VirtualClient who, TCommand command, params string[] skipCodes)
        where TCommand : IJournaledCommand
    {
        var outcome = await SubmitAsync(who, command).ConfigureAwait(false);
        if (outcome.IsAccepted) return outcome;
        if (outcome.IsRejected && outcome.Settlement!.ErrorCode is { } code && skipCodes.Contains(code, StringComparer.Ordinal))
            throw new ScenarioSkipped($"tenant precondition missing: {command.CommandTypeName} rejected with {code}.");
        throw Fail($"{command.CommandTypeName} was expected to be Accepted but: {outcome.Describe()}");
    }

    /// <summary>Submits and requires the server to refuse it (<c>Rejected</c>), optionally with a specific error code.</summary>
    public async Task<SubmitOutcome> RejectAsync<TCommand>(VirtualClient who, TCommand command, string? errorCode = null,
        string? expectedServerVersion = null)
        where TCommand : IJournaledCommand
    {
        var outcome = await SubmitAsync(who, command, expectedServerVersion).ConfigureAwait(false);
        if (!outcome.IsRejected) throw Fail($"{command.CommandTypeName} was expected to be Rejected but: {outcome.Describe()}");
        if (errorCode is not null && !string.Equals(outcome.Settlement!.ErrorCode, errorCode, StringComparison.Ordinal))
            throw Fail($"{command.CommandTypeName} was rejected with the wrong code (expected {errorCode}): {outcome.Describe()}");
        return outcome;
    }

    /// <summary>Submits a command carrying a deliberately stale version and requires the optimistic-concurrency conflict.</summary>
    public async Task<SubmitOutcome> ConflictAsync<TCommand>(VirtualClient who, TCommand command, string staleServerVersion)
        where TCommand : IJournaledCommand
    {
        var outcome = await SubmitAsync(who, command, staleServerVersion).ConfigureAwait(false);
        var conflict = outcome.IsConflict || outcome.Settlement?.ErrorCode == "CONCURRENCY_CONFLICT";
        if (!conflict) throw Fail($"{command.CommandTypeName} with a stale version was expected to settle as Conflict but: {outcome.Describe()}");
        return outcome;
    }

    /// <summary>Best-effort submit used by cleanup: never throws, returns whether the server accepted it.</summary>
    public async Task<bool> TrySubmitAsync<TCommand>(VirtualClient who, TCommand command)
        where TCommand : IJournaledCommand
    {
        try
        {
            var envelope = who.BuildEnvelope(command);
            var outcome = await who.SubmitWithRetryAsync(envelope, ct: Ct).ConfigureAwait(false);
            return outcome.IsAccepted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static string Brief(SubmitOutcome o) => o.Settlement is { } s
        ? $"{s.Outcome}{(s.ErrorCode is null ? string.Empty : $" [{s.ErrorCode}]")}{(s.ServerEntityId is null ? string.Empty : $" id={s.ServerEntityId}")}"
        : $"transport-error {o.TransportError}";

    // ───────────────────────────── pull + convergence ─────────────────────────────

    /// <summary>One pull of the state feed; server back-pressure is retried briefly, protocol failures fail the scenario.</summary>
    public async Task PullAsync(VirtualClient who)
    {
        PullResult result = default!;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            result = await who.PullAsync(Ct).ConfigureAwait(false);
            if (result.Kind != PullKind.Busy) break;
            await Task.Delay(TimeSpan.FromSeconds(3), Ct).ConfigureAwait(false);
        }

        if (result.Kind == PullKind.Failed) throw Fail($"client {who.Label} pull failed: {result.Error}");
        if (result.Kind == PullKind.Busy) throw Fail($"client {who.Label} pull stayed saturated (503) after retries: {result.Error}");
    }

    /// <summary>Pulls until <paramref name="until"/> holds for the row (or the convergence timeout elapses = desync).</summary>
    public async Task<ReplicaRow> AwaitRowAsync(VirtualClient who, string entity, string id, Func<ReplicaRow, bool>? until = null,
        string? what = null)
    {
        var started = DateTimeOffset.UtcNow;
        ReplicaRow? last = null;
        while (true)
        {
            await PullAsync(who).ConfigureAwait(false);
            if (who.Replica.TryGet(entity, id, out var row))
            {
                last = row;
                if (until is null || until(row))
                {
                    Log($"{who.Label} sees {entity}/{id} v={row.Version} after {(DateTimeOffset.UtcNow - started).TotalSeconds:0.0}s");
                    return row;
                }
            }

            if (DateTimeOffset.UtcNow - started > ConvergenceTimeout)
                throw Fail($"DESYNC: client {who.Label} never saw {entity}/{id} {what ?? "in the expected state"} within {ConvergenceTimeout.TotalSeconds:0}s " +
                           $"(row {(last is null ? "absent" : $"present v={last.Version} payload={last.Payload}")}; watermark {who.Replica.Watermark}).");
            await Task.Delay(1000, Ct).ConfigureAwait(false);
        }
    }

    /// <summary>Pulls until some row of <paramref name="entity"/> satisfies the predicate; returns it.</summary>
    public async Task<(string Id, ReplicaRow Row)> AwaitFindAsync(VirtualClient who, string entity, Func<string, ReplicaRow, bool> predicate,
        string what)
    {
        var started = DateTimeOffset.UtcNow;
        while (true)
        {
            await PullAsync(who).ConfigureAwait(false);
            if (who.Replica.Find(entity, predicate) is { } hit)
            {
                Log($"{who.Label} sees {entity}/{hit.Id} ({what}) after {(DateTimeOffset.UtcNow - started).TotalSeconds:0.0}s");
                return hit;
            }

            if (DateTimeOffset.UtcNow - started > ConvergenceTimeout)
                throw Fail($"DESYNC: client {who.Label} never saw a {entity} matching '{what}' within {ConvergenceTimeout.TotalSeconds:0}s (watermark {who.Replica.Watermark}).");
            await Task.Delay(1000, Ct).ConfigureAwait(false);
        }
    }

    /// <summary>Pulls until the row is gone (soft-deleted / tombstoned) for the client.</summary>
    public async Task AwaitGoneAsync(VirtualClient who, string entity, string id)
    {
        var started = DateTimeOffset.UtcNow;
        while (true)
        {
            await PullAsync(who).ConfigureAwait(false);
            if (!who.Replica.TryGet(entity, id, out _))
            {
                Log($"{who.Label} no longer sees {entity}/{id}");
                return;
            }

            if (DateTimeOffset.UtcNow - started > ConvergenceTimeout)
                throw Fail($"DESYNC: client {who.Label} still sees deleted {entity}/{id} after {ConvergenceTimeout.TotalSeconds:0}s.");
            await Task.Delay(1000, Ct).ConfigureAwait(false);
        }
    }

    /// <summary>Both A and B must hold the row, satisfying the predicate, at the SAME server version.</summary>
    public async Task<ReplicaRow> AwaitConvergedAsync(string entity, string id, Func<ReplicaRow, bool>? until = null, string? what = null)
    {
        var a = await AwaitRowAsync(A, entity, id, until, what).ConfigureAwait(false);
        var b = await AwaitRowAsync(B, entity, id, until, what).ConfigureAwait(false);
        if (a.Version == b.Version) return b;

        // A and B pulled at different instants: pull both once more before declaring divergence.
        await PullAsync(A).ConfigureAwait(false);
        await PullAsync(B).ConfigureAwait(false);
        var hasA = A.Replica.TryGet(entity, id, out var finalA);
        var hasB = B.Replica.TryGet(entity, id, out var finalB);
        Check(hasA && hasB && finalA.Version == finalB.Version,
            $"DESYNC: {entity}/{id} differs between clients — A v={(hasA ? finalA.Version : "absent")}, B v={(hasB ? finalB.Version : "absent")}.");
        return finalB;
    }

    // ───────────────────────────── plain reads ─────────────────────────────

    /// <summary>Operation-id → server-id map (<c>GET /api/commands/{op}/ids</c>): slot, entity type, server id.</summary>
    public async Task<IReadOnlyList<(int Slot, string EntityType, string ServerId)>> GetIdMapAsync(VirtualClient who, string operationId)
    {
        var (status, body, _) = await who.GetAsync($"api/commands/{operationId}/ids", Ct).ConfigureAwait(false);
        Check(status == System.Net.HttpStatusCode.OK, $"GET api/commands/{operationId}/ids answered {(status is null ? "no response" : ((int)status).ToString(CultureInfo.InvariantCulture))}: {body}");
        using var doc = JsonDocument.Parse(body);
        var list = new List<(int, string, string)>();
        if (doc.RootElement.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
            foreach (var item in ids.EnumerateArray())
                list.Add((item.GetProperty("slot").GetInt32(), item.GetProperty("entityType").GetString() ?? string.Empty,
                    item.GetProperty("serverEntityId").GetString() ?? string.Empty));
        return list;
    }

    /// <summary>Finds an invoice (and its payments) in the authoritative <c>/finance/ledger</c> read, scanning pages.</summary>
    public async Task<(FinanceInvoiceRow Invoice, IReadOnlyList<FinancePaymentRow> Payments)> ReadInvoiceAsync(VirtualClient who, string invoiceId)
    {
        // Try a recent cursor first (cheap), then fall back to a full scan from the beginning.
        foreach (var start in new[] { DateTimeOffset.UtcNow.AddHours(-1).UtcTicks, 0L })
        {
            var since = start;
            var payments = new List<FinancePaymentRow>();
            FinanceInvoiceRow? found = null;
            for (var page = 0; page < 200; page++)
            {
                var (status, body, _) = await who.GetAsync($"finance/ledger?since={since.ToString(CultureInfo.InvariantCulture)}&take=200", Ct)
                    .ConfigureAwait(false);
                Check(status == System.Net.HttpStatusCode.OK, $"GET finance/ledger answered {(status is null ? "no response" : ((int)status).ToString(CultureInfo.InvariantCulture))}: {body}");
                var data = JsonSerializer.Deserialize<FinanceReadPage>(body, Json) ?? throw Fail("finance/ledger returned an empty body.");
                foreach (var inv in data.Invoices.Where(i => i.Id == invoiceId)) found = inv;
                payments.AddRange(data.Payments.Where(p => p.InvoiceId == invoiceId));
                if (!data.HasMore || data.NextCursorTicks <= since) break;
                since = data.NextCursorTicks;
            }

            if (found is not null) return (found, payments);
        }

        throw Fail($"invoice {invoiceId} is not visible in the authoritative /finance/ledger read.");
    }

    /// <summary>Polls the authoritative ledger read until the invoice satisfies <paramref name="until"/> (the read model may lag the write briefly).</summary>
    public async Task<(FinanceInvoiceRow Invoice, IReadOnlyList<FinancePaymentRow> Payments)> AwaitInvoiceAsync(VirtualClient who, string invoiceId,
        Func<FinanceInvoiceRow, IReadOnlyList<FinancePaymentRow>, bool> until, string what)
    {
        var started = DateTimeOffset.UtcNow;
        FinanceInvoiceRow? last = null;
        while (true)
        {
            try
            {
                var (invoice, payments) = await ReadInvoiceAsync(who, invoiceId).ConfigureAwait(false);
                last = invoice;
                if (until(invoice, payments))
                {
                    Log($"ledger shows invoice {invoiceId}: {what} (state={invoice.State}, total={invoice.GrandTotalMinor}, paid={invoice.PaidTotalMinor}, due={invoice.DueTotalMinor})");
                    return (invoice, payments);
                }
            }
            catch (ScenarioFailure) when (DateTimeOffset.UtcNow - started <= ConvergenceTimeout)
            {
                // not visible yet — keep polling until the timeout
            }

            if (DateTimeOffset.UtcNow - started > ConvergenceTimeout)
                throw Fail($"LEDGER MISMATCH: invoice {invoiceId} never reached '{what}' in /finance/ledger within {ConvergenceTimeout.TotalSeconds:0}s " +
                           $"(last: {(last is null ? "not visible" : $"state={last.State} total={last.GrandTotalMinor} paid={last.PaidTotalMinor} due={last.DueTotalMinor}")}).");
            await Task.Delay(1000, Ct).ConfigureAwait(false);
        }
    }

    // ───────────────────────────── cleanup ─────────────────────────────

    /// <summary>Registers a best-effort teardown action; actions run in reverse order after the suite.</summary>
    public void AddCleanup(string name, Func<Task> action) => _cleanup.Push((name, action));

    /// <summary>Runs every registered teardown; returns (done, skipped). Failures are reported, never thrown.</summary>
    public async Task<(int Done, int Skipped)> RunCleanupAsync()
    {
        int done = 0, skipped = 0;
        while (_cleanup.Count > 0)
        {
            var (name, action) = _cleanup.Pop();
            try
            {
                await action().ConfigureAwait(false);
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped++;
                Console.WriteLine($"      cleanup skipped ({name}): {ex.Message}");
            }
        }

        return (done, skipped);
    }
}

/// <summary>Case-insensitive accessors over a replicated row's JSON payload.</summary>
public static class ReplicaRowExtensions
{
    public static JsonElement? Prop(this ReplicaRow row, string name)
    {
        if (row.Payload is not { ValueKind: JsonValueKind.Object } payload) return null;
        foreach (var property in payload.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }

    public static string? Str(this ReplicaRow row, string name) =>
        row.Prop(name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static long? Num(this ReplicaRow row, string name) =>
        row.Prop(name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : null;

    /// <summary>Sale price (minor units) of the product's first variant, or null when the replicated shape does not expose it.</summary>
    public static long? FirstVariantSalePriceMinor(this ReplicaRow row)
    {
        if (row.Prop("variants") is not { ValueKind: JsonValueKind.Array } variants) return null;
        foreach (var variant in variants.EnumerateArray())
        {
            if (variant.ValueKind != JsonValueKind.Object) continue;
            foreach (var property in variant.EnumerateObject())
                if (property.Name.Equals("salePriceMinor", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var n))
                    return n;
        }

        return null;
    }
}
