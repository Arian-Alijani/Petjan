# Plan — Fix `/sync/batches` 500 (pull fails → Sync reports "failed")

> Self-contained. A fresh model can implement from THIS FILE ALONE without re-reading the
> whole repo. Paths, root cause, exact code, and the fix are all inlined below.
> **Branch:** `genspark_ai_developer`. **After EVERY task below → run `scripts/git/push.sh "<msg>"` (MANDATORY).**
> No local build/test is possible in the sandbox (no `dotnet`, no Postgres). Verification is done by
> the repo's GitHub Actions CI/test system, which runs the PG-gated tests on push.

---

## 1. Symptom (from the user's client LIVE TRACE log)

- Client app connects fine: `/health/live` 200, `/auth/login` 200, `/auth/refresh` 200,
  `/auth/capabilities` 200, `/licensing/*` 200, `/sync/manifest` 200.
- Commands PUSH fine: `POST /api/commands` → 200 `outcome:0` (owner/pet/invoice created server-side).
- **PULL fails every time:** `POST /sync/batches` with body `{"epoch":1,"fromSequence":0}` →
  **`500 Internal Server Error` `{"code":"UNEXPECTED","detail":null,...}`** on EVERY attempt
  (requests #4, #13, #15, #19, #21, #23, #26, #28, #32, #35, #37, #39 in the trace).
- Because pull never succeeds, the app's "Sync" shows **failed**. This is the whole bug.
- One transport error (#29 `ResponseEnded`) is unrelated network noise; the retry (#30/#33) succeeded.
  The ONLY systematic server fault is the `500 UNEXPECTED` on `/sync/batches`.

The 500 appears even on the FIRST pull (#4, before the user created any patient), i.e. against the
already-seeded tenant (Tenant + RoleDefinition + UnitOfMeasure + ProductCategory + Supplier + Insurer +
LedgerAccount + AccountingPeriod + Rooms, etc. — the production seed set; `LastSequence`≈13 per the
`licensing/renew currentSequence:13` in the trace).

---

## 2. Root cause (CONFIRMED by code reading — no further investigation needed)

`POST /sync/batches` → `SyncEndpoints.Open` → `BatchFreezer.OpenAsync` → `Freeze` →
`RecordProjector.ReadAsync(...)` projects each journaled row through `Project(entity, row)`.

**`GlobalExceptionMiddleware` (src/Animora.Server/Hosting/GlobalExceptionMiddleware.cs) maps ANY
unhandled exception that is NOT pool-exhaustion/QueryBudget/Authz-timeout/AdmissionSaturated to
`500` with code `ServerErrorCodes.Unexpected` and `Detail=null` outside Development.** That is EXACTLY
the `{"code":"UNEXPECTED","detail":null}` body in the trace. `SyncEndpoints.Open`'s generic `catch`
logs then **re-throws**, so any throw inside the projector/freezer becomes this 500.

A prior partial fix (commits `ce177418`, `fee00e82`) added:
- a **row-level** guard in `RecordProjector.ReadAsync` for missing/unparseable `Version` / `UpdatedAt` /
  `Deleted` (emits a tombstone instead of throwing), and
- `AdmissionSaturatedException → 503` + batch-fault logging.

**That guard is INCOMPLETE.** The per-field projection in `RecordProjector.Project(...)` is still
UNGUARDED and can throw, which still produces the 500. The unguarded throw sites in
`src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs` `Project(...)` are:

```csharp
// money shape: {name}Minor present → reads {name}Currency unconditionally
else if (row.TryGetProperty(name + "Minor", out var minor))
    allowed[name] = new { minor = minor.GetInt64(),                         // (A) throws if Minor is JSON null
                          currency = row.GetProperty(name + "Currency").GetString() }; // (B) KeyNotFoundException if Currency col absent
// quantity shape: {name}Scaled present → reads {name}Unit unconditionally
else if (row.TryGetProperty(name + "Scaled", out var scaled))
    allowed[name] = new { value = scaled.GetInt64() / 1000m,                // (C) throws if Scaled is JSON null
                          unitCode = row.GetProperty(name + "Unit").GetString() };     // (D) KeyNotFoundException if Unit col absent
```

And in `ReadAsync` the surviving unguarded call after the row-level guard passes:

```csharp
yield return new(d.Name, id, 'U', version, updatedAt.GetDateTimeOffset(), // (E) throws if UpdatedAt string is not ISO-8601 parseable
                 deleted.GetBoolean(), Project(d.Name, row));             // (F) Project(...) throws per (A)-(D) above
```

Additional latent throw in `Project` → `Clean` (object branch):
```csharp
.ToDictionary(x => JsonNamingPolicy.CamelCase.ConvertName(x.Name), ...)   // (G) ArgumentException on duplicate camelCased key
```

**Why the existing PG tests did NOT catch it:** every PG-gated test
(`tests/Animora.Server.Tests/Sync/SyncPgRig.cs` → `Initialize()`) seeds **only a single `Tenant`
row**. `SyncBatchesResilienceTests` therefore proves the healthy Tenant path but NEVER exercises the
projector against a row carrying a money/quantity value object, an enum, a permission array, or a
richer production entity. So the real crash path is untested. Client-side
`tests/Animora.Sync.Tests/HttpStateSyncEnginePullTests.cs` uses a scripted fake server and never runs
the real projector at all.

**Net:** the fix must make the projector *never throw out of the batch build for a single row/field*.
Whatever the exact offending field on the user's tenant is, hardening these sites fixes it AND prevents
the entire class of "one bad row 500s the whole pull" regressions. This is the correct, minimal,
robust fix; it keeps the feed live (a bad row degrades to a safe tombstone, which the client converges
by dropping its stale copy), matching the design intent already written in the BUG 2 comments.

---

## 3. Files that matter (only these)

| File | Role |
|---|---|
| `src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs` | **FIX HERE.** `ReadAsync` loop + `Project` + `Clean`. |
| `src/Animora.Server/Sync/SyncEndpoints.cs` | Endpoint; generic catch logs + rethrows → 500. (No change needed; already logs FailureClass.) |
| `src/Animora.Server/Hosting/GlobalExceptionMiddleware.cs` | Confirms UNEXPECTED mapping. (No change.) |
| `tests/Animora.Server.Tests/Sync/SyncPgRig.cs` | Test rig; only seeds a Tenant. **Extend: add a seed helper for a money/qty entity.** |
| `tests/Animora.Server.Tests/Sync/SyncBatchesResilienceTests.cs` | **Add reproducing test here.** |
| `src/Animora.Persistence.PostgreSql/Replication/SyncContractCatalog.cs` | Projection order + child descriptors (reference only). |

Client side is COHERENT — no client change required. The pull engine
(`Animora.Sync.Custom` / `HttpStateSyncEngine`) correctly surfaces the server 500 as a sync failure;
once the server returns a valid `BatchReady`, the client applies it. Do NOT touch client code.

---

## 4. The fix (exact, drop-in)

### 4.1 Harden `Project(...)` so no single field can throw — `RecordProjector.cs`

Replace the money/quantity/Vitals extraction block inside `Project` (the `foreach (var property ...)`
loop body) so each shaped field is read defensively:

- money (`{name}Minor`): only emit when `Minor` is a real number AND `{name}Currency` is a string;
  otherwise skip the field (treat as absent). Never call `GetInt64()` on null / `GetProperty` on a
  missing key.
- quantity (`{name}Scaled`): same guard with `{name}Unit`.
- keep the existing direct-match (`Clean`) and `Vitals` branches.

Concretely, the loop becomes (helper `TryNumber`/`TryString` added):

```csharp
foreach (var property in contract.GetProperties())
{
    var name = property.Name;
    if (row.TryGetProperty(name, out var value)) allowed[name] = Clean(value);
    else if (row.TryGetProperty(name + "Minor", out var minor))
    {
        if (TryInt64(minor, out var m) && TryString(row, name + "Currency", out var cur))
            allowed[name] = new { minor = m, currency = cur };
        // else: optional money is null/absent → field omitted (client treats as null)
    }
    else if (row.TryGetProperty(name + "Scaled", out var scaled))
    {
        if (TryInt64(scaled, out var s) && TryString(row, name + "Unit", out var unit))
            allowed[name] = new { value = s / 1000m, unitCode = unit };
    }
    else if (name == "Vitals")
        allowed[name] = new { weightKg = row.TryGetProperty("Vitals_WeightKg", out var w) ? Clean(w) : null,
            temperatureC = row.TryGetProperty("Vitals_TemperatureC", out var t) ? Clean(t) : null };
}
```

Add these static helpers to `RecordProjector`:

```csharp
private static bool TryInt64(JsonElement e, out long value)
{
    value = 0;
    return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out value);
}
private static bool TryString(JsonElement row, string prop, out string value)
{
    value = string.Empty;
    if (!row.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.String) return false;
    value = e.GetString()!;
    return true;
}
```

### 4.2 Make per-row projection fault-isolated — `RecordProjector.ReadAsync`

Even with 4.1, keep the batch alive if `Project` or `GetDateTimeOffset()` ever throws for one row:
emit a tombstone for that single row instead of letting it bubble to a 500. Replace the final emit in
the `while (await reader.ReadAsync...)` loop:

```csharp
// after the existing Version/UpdatedAt/Deleted row-level guard that `continue`s with a tombstone:
SyncRecord record;
try
{
    record = new(d.Name, id, 'U', version, updatedAt.GetDateTimeOffset(),
                 deleted.GetBoolean(), Project(d.Name, row));
}
catch
{
    // Defence in depth: a single row whose field shape is unexpectedly off (schema drift, a
    // bad timestamp, a money/qty column we failed to anticipate) degrades to a tombstone so the
    // whole /sync/batches build never 500s. Client converges by dropping any stale copy.
    record = new(d.Name, id, 'D', null, null, true, null);
}
yield return record;
```

> NOTE: a `yield return` cannot sit inside a `try/catch` that has a `catch`. So build `record` inside
> the `try/catch` (which contains NO yield) and `yield return record;` AFTER the try/catch, exactly as
> written above. This compiles.

### 4.3 Keep `Clean` object branch from throwing on duplicate camelCase keys (defensive, cheap)

In `Clean`, the object branch uses `ToDictionary(camelCaseName, ...)`. Guard against a duplicate key
collision by using an overwrite-last grouping instead of `ToDictionary` (which throws on dupes):

```csharp
JsonValueKind.Object => value.EnumerateObject()
    .Where(x => x.Name != "TenantId" && x.Name != "tenantId")
    .OrderBy(x => x.Name, StringComparer.Ordinal)
    .GroupBy(x => JsonNamingPolicy.CamelCase.ConvertName(x.Name))
    .ToDictionary(g => g.Key, g => Clean(g.Last().Value)),
```

(4.3 is low-risk hardening; if it complicates the diff, 4.2's blanket per-row catch already prevents the
500, so 4.2 is the non-negotiable part. Do 4.1 + 4.2 for sure; 4.3 is nice-to-have.)

---

## 5. Reproducing / regression test (server-side, PG-gated)

Goal: a test that FAILS on the old projector (throws → not `BatchReady`) and PASSES after the fix.

### 5.1 Extend `SyncPgRig.cs` with a money/quantity seed helper

Add a method that inserts one `Products` row carrying money + quantity value objects (the shape that
exercises the money/qty projection path). Column names (verified in migrations):
`Products` has `ReorderLevelScaled`,`ReorderLevelUnit`,`DefaultCostPriceMinor`,`DefaultCostPriceCurrency`,
`DefaultSalePriceMinor`,`DefaultSalePriceCurrency`, plus `Sku`,`Name`,`CategoryId`,`Kind`,`UnitCode`,
`IsPrescriptionOnly`,`IsActive`,`TrackStock`,`Id`,`TenantId`,`UpdatedAt`,`Version`,`Deleted`.
Requires a `ProductCategories` row first (FK/`CategoryId`) and a `UnitsOfMeasure` row for `UnitCode`.

Add to `SyncPgRig`:

```csharp
public async Task SeedProduct()
{
    await using var db = Factory.Create();
    var cat = IdGenerator.NewId(DateTimeOffset.UtcNow);
    var prod = IdGenerator.NewId(DateTimeOffset.UtcNow);
    // Minimal rows. Adjust column lists to match the 108_MoneyStock migration exactly.
    await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO public.""ProductCategories""
        (""Id"",""TenantId"",""Name"",""UpdatedAt"",""Deleted"") VALUES ({cat},{Tenant},'Food',now(),false)");
    await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO public.""Products""
        (""Id"",""TenantId"",""Sku"",""Name"",""CategoryId"",""Kind"",""UnitCode"",
         ""IsPrescriptionOnly"",""IsActive"",""TrackStock"",
         ""ReorderLevelScaled"",""ReorderLevelUnit"",
         ""DefaultCostPriceMinor"",""DefaultCostPriceCurrency"",
         ""DefaultSalePriceMinor"",""DefaultSalePriceCurrency"",""UpdatedAt"",""Deleted"")
        VALUES ({prod},{Tenant},'SKU1','Kibble',{cat},0,'unit',false,true,true,
         5000,'unit',12345,'IRT',20000,'IRT',now(),false)");
}
```

> If any NOT NULL column is missing from the INSERT, CI will error with the exact column — add it.
> Do NOT guess optional columns; keep the INSERT minimal and let CI name any missing required column.
> `Version` may have a DB default (the Tenant seed omits it and works); if CI complains, add
> `""Version""` with `gen_random_uuid()` or a literal GUID.

### 5.2 Add the reproducing test in `SyncBatchesResilienceTests.cs`

```csharp
[PgFact]
public async Task TenantWithMoneyAndQuantityRows_OpenBatch_ReturnsBatchReady_NotFault()
{
    await using var rig = new SyncPgRig(pg.ConnectionString);
    await rig.Initialize();
    await rig.SeedProduct(); // money + quantity value objects — the shape that used to 500

    var result = await rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None);

    Assert.Equal("BatchReady", result.Kind);
    Assert.NotNull(result.Batch);
    var records = await rig.Download(result.Batch!);
    Assert.Contains(records, r => r.Entity == "Product"); // projected, not dropped
}
```

This test, against the UNFIXED projector, would throw inside `Project` (money/qty) and the freezer
would surface a non-`BatchReady` / exception → test red. After 4.1/4.2 it is green.

---

## 6. Execution order & push discipline (LOCKED)

Run `scripts/git/push.sh "<conventional message>"` after EACH task. If it exits `3`, call the
`setup_github_environment` tool once, then re-run. Never leave a task un-pushed.

- [x] **T0** — Investigate client log + server sync path; confirm root cause. (DONE — see §2; evidence:
      GlobalExceptionMiddleware UNEXPECTED mapping + unguarded `Project` money/qty/`GetDateTimeOffset`;
      PG tests seed only a Tenant so the path is untested.)
- [x] **T1** — Create `Plan/` + write this plan. **push.** (this file)
- [x] **T2** — Implement §4.1 + §4.2 in `RecordProjector.cs` (and §4.3 if clean). **push**
      `fix(sync): harden projector so a single bad field/row never 500s /sync/batches`.
- [x] **T3** — Add §5.1 `SeedProduct` to `SyncPgRig.cs` + §5.2 reproducing test to
      `SyncBatchesResilienceTests.cs`. **push**
      `test(sync): /sync/batches stays BatchReady for money/quantity rows (repro)`.
- [x] **T4** — (CI feedback loop) If GitHub Actions reports a compile error (e.g. missing NOT NULL
      column in the INSERT, or `yield`-in-`try` CS1626), fix precisely per the CI message and **push**.
      Repeat until CI sync tests are green. Keep each fix small; if a heavy fix lands, push it with a
      clear message.
- [ ] **T5 (FINAL)** — Delete ONLY this file `Plan/sync-batches-500-fix.md`. Do NOT touch any other
      file or any roadmap file. **push** `chore(plan): remove completed sync-batches-500 plan`.

---

## 7. Acceptance criteria

1. `POST /sync/batches {"epoch":1,"fromSequence":0}` against a seeded tenant returns a `BatchReady`
   (or `UpToDate`) — never `500 UNEXPECTED`.
2. A row whose field shape is unexpected degrades to a tombstone; the batch still builds (feed stays live).
3. New PG test `TenantWithMoneyAndQuantityRows_OpenBatch_ReturnsBatchReady_NotFault` passes in CI.
4. No client-side change. No change to projection order, wire contract, or command path.
5. Every task above was pushed via `scripts/git/push.sh`.
