using System.Globalization;
using Animora.Application.UseCases.Inventory;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Purchasing;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Sales;
using Animora.Application.Commands;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.LiveTest.Client;
using Animora.LiveTest.Config;
using Animora.LiveTest.Correctness;

namespace Animora.LiveTest.Capacity;

/// <summary>One logged-in desktop session of the load pool plus the lock that serialises pulls on its (non thread-safe) replica.</summary>
public sealed class PoolClient(VirtualClient client)
{
    public VirtualClient Client { get; } = client;

    public SemaphoreSlim PullLock { get; } = new(1, 1);
}

/// <summary>
/// Everything a load profile needs while running: the device pool, shared reference data created once in
/// <see cref="LoadProfile.Setup"/>, and helpers that run REAL commands through the production transport and classify the answer.
/// Created rows are namespaced <c>lt-cap&lt;run&gt;-…</c> and live only in the dedicated test tenant.
/// </summary>
public sealed class LoadContext(LiveTestOptions options, string runId, IReadOnlyList<PoolClient> pool, CancellationToken ct)
{
    private long _counter;

    public LiveTestOptions Options { get; } = options;

    public string RunId { get; } = runId;

    public IReadOnlyList<PoolClient> Pool { get; } = pool;

    public CancellationToken Ct { get; } = ct;

    // Reference data prepared by profile setups (read-only while load runs).
    public OwnerPet? OwnerPet { get; set; }

    public ProductFixture? Product { get; set; }

    /// <summary>Device session of virtual user <paramref name="worker"/>. Users beyond the pool size share sessions round-robin.</summary>
    public PoolClient For(int worker) => Pool[worker % Pool.Count];

    public string Name(string kind) => $"lt-{RunId}-{kind}-{Interlocked.Increment(ref _counter)}";

    /// <summary>Unique 11-digit mobile number (counter based, so concurrent users never collide on owner de-duplication).</summary>
    public string Phone() => "09" + (100_000_000 + Interlocked.Increment(ref _counter) * 7 + Random.Shared.Next(0, 6)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Submits one real command (Deferred answers are retried with the same operation id, like the production processor).</summary>
    public async Task<SubmitOutcome> SubmitAsync<TCommand>(PoolClient who, TCommand command)
        where TCommand : IJournaledCommand
    {
        var envelope = who.Client.BuildEnvelope(command);
        return await who.Client.SubmitWithRetryAsync(envelope, maxAttempts: 4, ct: Ct).ConfigureAwait(false);
    }

    /// <summary>Accepted ⇒ Ok; still Deferred (429/503 back-pressure) ⇒ ExpectedBusy; anything else ⇒ Unexpected.</summary>
    public static Outcome Classify(SubmitOutcome outcome) =>
        outcome.IsAccepted ? Outcome.Ok : outcome.IsDeferred ? Outcome.ExpectedBusy : Outcome.Unexpected;

    /// <summary>One state-feed pull on the worker's session; 503 back-pressure is ExpectedBusy.</summary>
    public async Task<Outcome> PullAsync(PoolClient who, bool forceSnapshot = false)
    {
        await who.PullLock.WaitAsync(Ct).ConfigureAwait(false);
        try
        {
            if (forceSnapshot) who.Client.Replica.Reset();
            var result = await who.Client.PullAsync(Ct).ConfigureAwait(false);
            return result.Kind switch
            {
                PullKind.Applied or PullKind.UpToDate => Outcome.Ok,
                PullKind.Busy => Outcome.ExpectedBusy,
                _ => Outcome.Unexpected,
            };
        }
        finally
        {
            who.PullLock.Release();
        }
    }
}

/// <summary>A realistic weighted mix of real operations. <see cref="Setup"/> runs once; <see cref="Operation"/> runs per measured request.</summary>
public sealed record LoadProfile(
    string Name,
    string Description,
    Func<LoadContext, Task> Setup,
    Func<LoadContext, int, int, Task<Outcome>> Operation);

/// <summary>The built-in capacity profiles. Each operation is what a real desktop does, over the real wire protocol.</summary>
public static class LoadProfiles
{
    public static IReadOnlyList<LoadProfile> All { get; } =
    [
        new("reception-day",
            "Read-heavy front desk: 60% incremental state pulls (lookups), 30% walk-in tickets, 10% new owner + pet.",
            NoSetup, ReceptionDayAsync),
        new("sales-rush",
            "Write-heavy money path: each request = create invoice → add line → post → register payment (4 commands).",
            SetupOwnerAsync, SalesRushAsync),
        new("inventory-receipt",
            "Stock desk: 50% stock adjust, 30% product price update, 20% new supplier.",
            SetupProductAsync, InventoryAsync),
        new("sync-pull",
            "State feed: incremental pulls, with a forced full snapshot every 10th request.",
            NoSetup, SyncPullAsync),
    ];

    private static Task NoSetup(LoadContext c) => Task.CompletedTask;

    // ───────────────────────────── setups ─────────────────────────────

    private static async Task SetupOwnerAsync(LoadContext c) =>
        c.OwnerPet ??= await SetupFixtures(c).OwnerPetAsync().ConfigureAwait(false);

    private static async Task SetupProductAsync(LoadContext c) =>
        c.Product ??= await SetupFixtures(c).ProductAsync().ConfigureAwait(false);

    /// <summary>Reuses the correctness <see cref="Fixtures"/> (same code paths, verified convergence) over the first and last pool sessions.</summary>
    private static Fixtures SetupFixtures(LoadContext c) =>
        new ScenarioContext(c.Options, "cap" + c.RunId, c.Pool[0].Client, c.Pool[^1].Client, c.Ct).Shared;

    // ───────────────────────────── operations ─────────────────────────────

    private static async Task<Outcome> ReceptionDayAsync(LoadContext c, int worker, int iteration)
    {
        var who = c.For(worker);
        switch (iteration % 10)
        {
            case < 6:
                return await c.PullAsync(who).ConfigureAwait(false);
            case < 9:
                return LoadContext.Classify(await c.SubmitAsync(who,
                    new CreateWalkInCommand(c.Name("pet"), c.Name("owner"), c.Phone(), null, false)).ConfigureAwait(false));
            default:
                return LoadContext.Classify(await c.SubmitAsync(who, new CreateOwnerCommand(c.Name("owner"), c.Phone(), OwnerGender.Male,
                    null, null, null, c.Name("pet"), PetSpecies.Dog, "mixed", "black", PetGender.Male, null, null, null, false)).ConfigureAwait(false));
        }
    }

    private static async Task<Outcome> SalesRushAsync(LoadContext c, int worker, int iteration)
    {
        var who = c.For(worker);
        var op = c.OwnerPet!;
        var created = await c.SubmitAsync(who, new CreateBlankInvoiceCommand(op.OwnerId, null, op.PetId)).ConfigureAwait(false);
        if (!created.IsAccepted) return LoadContext.Classify(created);

        var id = ScenarioContext.IdOf(created);
        var line = await c.SubmitAsync(who, new AddCustomInvoiceLineCommand(id, c.Name("svc"), 50_000, 1m)).ConfigureAwait(false);
        if (!line.IsAccepted) return LoadContext.Classify(line);

        var posted = await c.SubmitAsync(who, new PostInvoiceCommand(id)).ConfigureAwait(false);
        if (!posted.IsAccepted) return LoadContext.Classify(posted);

        return LoadContext.Classify(await c.SubmitAsync(who,
            new RegisterPaymentCommand(id, PaymentMethod.Transfer, Money.Irt(50_000), c.Name("pay"))).ConfigureAwait(false));
    }

    private static async Task<Outcome> InventoryAsync(LoadContext c, int worker, int iteration)
    {
        var who = c.For(worker);
        var p = c.Product!;
        switch (iteration % 10)
        {
            case < 5:
                return LoadContext.Classify(await c.SubmitAsync(who,
                    new AdjustStockCommand(p.ProductId, p.VariantId, 1_000, AdjustmentReasons.Count, "cap +1")).ConfigureAwait(false));
            case < 8:
            {
                long sale = 20_000 + iteration % 50;
                var variant = new ProductVariantInput(p.VariantId, p.Sku + "-1", ProductVariant.DefaultAttributeName, "default", 10_000, sale, 0);
                return LoadContext.Classify(await c.SubmitAsync(who,
                    new UpdateProductInfoCommand(p.ProductId, p.Name, 10_000, sale, [variant])).ConfigureAwait(false));
            }

            default:
                return LoadContext.Classify(await c.SubmitAsync(who,
                    new CreateSupplierCommand(c.Name("supplier"), null, 0, c.Phone())).ConfigureAwait(false));
        }
    }

    private static Task<Outcome> SyncPullAsync(LoadContext c, int worker, int iteration) =>
        c.PullAsync(c.For(worker), forceSnapshot: iteration % 10 == 9);
}
