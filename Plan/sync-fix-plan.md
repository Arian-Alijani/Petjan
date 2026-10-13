# Plan: Fix Animora Sync (BUG 1 push rejection + BUG 2 pull 500) & review sync coherence

> Self-contained execution plan. All root-cause analysis is already done and distilled below.
> A model reading this can start implementing immediately with **minimal re-investigation**.
>
> **LOCKED RULE — push after every task:** After completing EACH task below, run
> `scripts/git/push.sh "type(scope): message"` (commit + sync + push). No exceptions.
> If push exits code `3` (no creds) → call the `setup_github_environment` tool once, then re-run.
> Work on branch `genspark_ai_developer`.

---

## 0. Context you need (no re-reading required)

**Repo:** `/home/user/webapp` — Animora (veterinary clinic app). .NET solution.
- Server: `src/Animora.Server` + `src/Animora.Persistence.PostgreSql`.
- Desktop client sync engine: `src/Animora.Sync.Custom`, command pipeline: `src/Animora.Application`.
- Wire contracts: `src/Animora.Contracts.Wire`.

**Sync shape (standard):**
- Push: client → `POST /api/commands` (single) / batch. Envelope carries `CommandType`,
  `AggregateType`, `AggregateId`, `PayloadJson`, `CommandFingerprint`, identity, sequence.
- Pull: client → `POST /sync/batches {epoch, fromSequence}` → server freezes an immutable
  gzip batch; client then GETs pages. Also `GET /sync/manifest`, `POST /sync/snapshots`.
- Wire contract version header `X-Animora-Contract-Version: 2` — client sends it correctly
  (`WireContract.Version = 2`), server `SyncContractFilter` requires `== "2"`. ✔ coherent.

**Two independent defects (both confirmed by code reading):**

| # | Symptom | Layer | Status |
|---|---------|-------|--------|
| BUG 1 | Every command push → HTTP 200 `outcome=1 VALIDATION_FAILED "payload shape; ruleset=1"` | **client** payload serialization | Fully diagnosed, ready to fix |
| BUG 2 | Every `POST /sync/batches` → HTTP 500 `UNEXPECTED` | **server** `/sync/batches` handler (unhandled exception) | Candidate causes ranked; needs defensive fix + confirm via trace |

---

## BUG 1 — ROOT CAUSE (CONFIRMED, exact)

**Mechanism:** the client serializes the *entire command object* into `PayloadJson`, which
leaks three `IJournaledCommand` members that are NOT domain constructor parameters. The server
accepts a payload only when its JSON keys **exactly equal** the command's constructor parameter
names (set equality). The three extra keys break the match.

**Exact leak point (client):**
`src/Animora.Application/Behaviors/CommandJournalBehavior.cs:35`
```csharp
var payloadJson = CommandFingerprint.CanonicalJson(request); // serializes WHOLE command
```
`CommandFingerprint.CanonicalJson<T>` (`src/Animora.Application/Commands/CommandFingerprint.cs:42`)
does `JsonSerializer.SerializeToNode(payload, SerializeOptions)` with `CamelCase`. For a command
record like `CreateOwnerCommand` (`src/Animora.Application/UseCases/Patients/CreateOwnerCommand.cs:18-29`)
it serializes all readable public props, including the three interface members:
```csharp
public string  CommandTypeName   => CommandTypes.PatientsOwnerCreate; // "commandTypeName"
public string  AggregateTypeName => "Owner";                          // "aggregateTypeName"
public string? AggregateIdOrNull => null;                             // "aggregateIdOrNull"
```
So `PayloadJson` = `{ctor args...} + {aggregateIdOrNull, aggregateTypeName, commandTypeName}`.

**Exact rejection point (server):**
`src/Animora.Server/Commands/CommandPayloadCatalog.cs`
- Builder (line 64-65): allowed `PropertyNames` = **longest constructor's parameter names only**.
- `Resolve` (line 34-37): `names.SetEquals(candidate.PropertyNames)` → the three extra keys →
  `matches.Length == 0` → `Fail(ValidationFailed, "payload shape")`.
- The `"; ruleset=1"` suffix is appended in
  `src/Animora.Server/Commands/CommandIntakeService.cs:115`.
- NOTE: the three fields ARE still validated as an identity check later in `Resolve` (line 39-41),
  but that reads them from the **envelope**, not the payload. Payload must carry ctor args only.

**Verified consistency facts (so the fix is safe):**
- `CommandFingerprint` = SHA-256 over `CommandType + 0x1F + PayloadJson`. Changing the payload
  shape changes the fingerprint, but client+server agree on the same `PayloadJson`, and nothing
  compares fingerprint against ctor shape. Fingerprint identity across retries is preserved
  because the SAME serialization path is used every time.
- `CanonicalJson` other callers are safe: `CommandIdRemapService.cs:148` canonicalizes an already
  parsed `JsonNode` (not a command POCO) → the modifier below won't touch it; `ProvisioningEngine.cs:57`
  serializes an anonymous object (not `IJournaledCommand`) → unaffected.
- Server deserializes payload with `JsonSerializerDefaults.Web` (case-insensitive) → camelCase
  ctor-arg keys bind fine after the extra keys are removed.

### FIX (centralized, one place, covers all 41 journaled commands)

In `src/Animora.Application/Commands/CommandFingerprint.cs`, add a
`System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver` with a type-info modifier
to `SerializeOptions` that **removes** the three journaling properties for any type assignable to
`IJournaledCommand`. Drop by property NAME (`CommandTypeName`, `AggregateTypeName`,
`AggregateIdOrNull`) so it is robust regardless of each record's declaration.

Sketch:
```csharp
private static readonly JsonSerializerOptions SerializeOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver
    {
        Modifiers = { DropJournalingMembers }
    },
};

private static readonly HashSet<string> JournalingMembers = new(StringComparer.Ordinal)
{ nameof(IJournaledCommand.CommandTypeName), nameof(IJournaledCommand.AggregateTypeName), nameof(IJournaledCommand.AggregateIdOrNull) };

private static void DropJournalingMembers(JsonTypeInfo info)
{
    if (info.Kind != JsonTypeInfoKind.Object || !typeof(IJournaledCommand).IsAssignableFrom(info.Type)) return;
    for (int i = info.Properties.Count - 1; i >= 0; i--)
        if (JournalingMembers.Contains(info.Properties[i].GetMemberName() ?? "")) // match CLR member name
            info.Properties.RemoveAt(i);
}
```
Matching note: `JsonPropertyInfo` exposes the CLR member via `GetMemberName()` (or compare the
camelCased `Name` to `commandTypeName`/`aggregateTypeName`/`aggregateIdOrNull`). Use whichever is
available in the project's STJ version; prefer CLR member name. Add required usings
(`System.Text.Json.Serialization.Metadata`, `Animora.Application.Commands`).

**Alternative (NOT chosen):** `[JsonIgnore]` on the three members in all ~41 command records — more
files, easy to miss new commands. Centralized modifier is superior.

### VERIFY BUG 1
- After fix, serialized `patients.owner.create` payload must contain EXACTLY:
  `address, fullName, gender, nationalId, petBirthUtc, petBreed, petColor, petGender,
  petMicrochip, petMicrochipUtc, petName, petNeutered, petSpecies, phonePrimary, phoneSecondary`
  — and nothing else.
- Add/extend a unit test in `tests/Animora.Application.Tests` (near `IdempotencyTests.cs` which
  already uses `CanonicalJson`): assert `CanonicalJson(new CreateOwnerCommand(...))` does NOT
  contain `commandTypeName`/`aggregateTypeName`/`aggregateIdOrNull`, and that the key set equals
  the ctor params. Do NOT run full solution test; target this one test project only if runnable
  quickly, else rely on the assertion-level reasoning + build of the project.

**STATUS: [ ] not started**

---

## BUG 2 — ROOT CAUSE (server 500 on /sync/batches)

**Flow (confirmed by reading):**
`SyncEndpoints.cs:26` → `Open(...)` → `BatchFreezer.OpenAsync` → `Freeze`
(`src/Animora.Persistence.PostgreSql/Replication/BatchFreezer.cs`).
`Open` only catches `SnapshotLifetimeExceededException` (→503). **Any other exception propagates
to the global handler → 500 `UNEXPECTED`.**

**What is RULED OUT (do not chase these):**
- Missing feed-state row: `TenantProvisioner.cs:77` seeds `TenantFeedStateRow` at provisioning;
  `GET /sync/manifest` succeeds returning `epoch:1` (same `FeedPositionReader`), so the row exists.
- `DataRootPath` unset: `FeedStorageOptions.DataRootPath` is wired from `ServerOptions.DataRootPath`
  at `src/Animora.Server/Program.cs:128-129`; `ServerOptions` is validated non-empty+absolute at
  startup (`ServerOptionsValidator.cs:18-24`) with a `DataRootWritableProbe`. On a healthy server
  the staging dir (`BatchFreezer.Freeze` line 43 → `FeedPageWriter.CreateWorkspace`) is writable.
- Contract header / 426: client sends `X-Animora-Contract-Version: 2` correctly.
- Fresh EMPTY tenant: `Decide` (BatchFreezer line 21-28) short-circuits `fromSequence==LastSequence`
  (both 0) → returns `UpToDate` 200 **before** the projector/`FOR UPDATE` path. So a truly empty
  tenant does NOT hit the heavy path.

**Therefore the 500 is in the batch-BUILD path, reached when `LastSequence > 0` (journal has rows),
or in response/row serialization.** Ranked candidate causes (all inside `Freeze`):

1. **Projection-row serialization / missing JSON property (MOST LIKELY).**
   `RecordProjector.ReadAsync` (`RecordProjector.cs:51-55`) does
   `row.GetProperty("Version")`, `row.GetProperty("UpdatedAt")`, `row.GetProperty("Deleted")`.
   If any projected table row image lacks one of those columns (schema drift, a projection-ordered
   entity without `Version`/`UpdatedAt`/`Deleted`, or a null), `GetProperty` throws
   `KeyNotFoundException` / `InvalidOperationException` → unhandled → 500. Also `Guid.Parse(... .GetString()!)`
   and `EntityVersionCodec.ToWire` can throw on malformed/empty values.
2. **`TenantFeedState FOR UPDATE` single-row read** (BatchFreezer line 83-84 `.SingleAsync`) throws
   if the row is absent in the write-connection snapshot (unlikely given manifest works, but the
   raw interpolated SQL + tenant filter should be double-checked).
3. **Child-projection SQL** (`RecordProjector.cs:27` `jsonb_agg(... ORDER BY ...::text)`) or a
   `ChildImage` column that does not exist in the DB → `PostgresException` → 500.
4. **`AdmissionSaturatedException`** (`IBackgroundAdmission.cs:32`) is NOT caught in
   `SyncEndpoints.Open` → would 500 under saturation (not deterministic, but must be mapped).

### STEP 2A — CONFIRM the exact exception (do this FIRST, cheap)
The 500 responses carry `X-Correlation-Id` (e.g. `01M4TBD5H3B16Y4DT8AKCSF34E`). The server logs the
stack trace under that id. If server logs are reachable, grep them:
```bash
grep -rn "01M4TBD5H3B16Y4DT8AKCSF34E" /var/log/* /opt/animora/* 2>/dev/null
# or: journalctl -u animora -f   while reproducing
```
If logs are NOT reachable in this environment, proceed by making the handler resilient AND by
adding a targeted log so the exception is captured next time (see Step 2C).

### STEP 2B — FIX: make `/sync/batches` fail-safe, never bare-500
Primary (robustness, do regardless of which candidate it is):
1. In `RecordProjector.ReadAsync`, null-guard the required JSON members:
   use `TryGetProperty` for `Version`/`UpdatedAt`/`Deleted`; if any is missing/null, treat the row
   as a tombstone/skip it (yield a `'D'` delete or `continue`) instead of throwing. This directly
   removes candidate #1's crash and is the fix the analysis recommends.
2. In `SyncEndpoints.Open` (`src/Animora.Server/Sync/SyncEndpoints.cs:33-49`), map
   `AdmissionSaturatedException` → `503 + Retry-After: 1` (same as `SnapshotLifetimeExceededException`),
   so overload sheds cleanly instead of 500 (candidate #4).
3. Ensure a fresh/empty build path returns a valid empty `BatchReady`/`UpToDate` 200, never throws.

### STEP 2C — Observability so a residual 500 is diagnosable
If the global handler does not already log the stack trace with the correlation id, add a narrow
`try/catch` in `BatchFreezer.OpenAsync` (or `SyncEndpoints.Open`) that logs
`ex.GetType().FullName + ex.Message + stack` at Error with the correlation id, then rethrows — do
NOT swallow. Keep it minimal.

### VERIFY BUG 2
- Reason through (and if quick, add) a focused test: a tenant WITH some journal rows calls
  `/sync/batches {epoch:1, fromSequence:0}` and gets 200 (`BatchReady` or `UpToDate`), never 500.
- A fresh tenant returns `UpToDate`/empty 200.
- Do NOT run the full test suite or long PG integration build. Target the specific project/test.

**STATUS: [ ] not started**

---

## Task list (execute top→bottom; PUSH after each)

- [ ] **T1 — BUG 1 fix.** Edit `src/Animora.Application/Commands/CommandFingerprint.cs`: add the
      `DefaultJsonTypeInfoResolver` modifier dropping `CommandTypeName`/`AggregateTypeName`/
      `AggregateIdOrNull` for `IJournaledCommand` types. Build ONLY `Animora.Application`
      (`dotnet build src/Animora.Application` — short). → **PUSH**
      `fix(sync): exclude journaling members from command payload (BUG 1 payload-shape)`

- [ ] **T2 — BUG 1 verification test.** Add a small test in `tests/Animora.Application.Tests`
      asserting the serialized `CreateOwnerCommand` payload key-set equals its ctor params and
      excludes the three journaling keys. Build/run just that test project if fast; otherwise keep
      it compile-checked. → **PUSH** `test(sync): assert command payload excludes journaling keys`

- [ ] **T3 — BUG 2 confirm.** Attempt to retrieve the real stack trace by correlation id (Step 2A).
      Record the finding inline in the commit message. (No code change if logs unreachable.)
      → **PUSH only if a code/doc change was made**, else skip push for this read-only step.

- [ ] **T4 — BUG 2 fix (projector null-guard).** Edit `RecordProjector.ReadAsync` to use
      `TryGetProperty` for `Version`/`UpdatedAt`/`Deleted` and skip/tombstone instead of throwing.
      Build ONLY `Animora.Persistence.PostgreSql`. → **PUSH**
      `fix(sync): null-guard projection rows so /sync/batches never 500s (BUG 2)`

- [ ] **T5 — BUG 2 fix (endpoint exception mapping).** In `SyncEndpoints.Open` map
      `AdmissionSaturatedException` → 503+Retry-After (alongside `SnapshotLifetimeExceededException`).
      Optional: minimal error-log-with-rethrow in `BatchFreezer.OpenAsync` for residual diagnosis.
      Build ONLY the two affected projects. → **PUSH**
      `fix(sync): map admission saturation to 503 and log batch build faults (BUG 2)`

- [ ] **T6 — BUG 2 verification test.** Focused test: tenant with journal rows →
      `/sync/batches {1,0}` returns 200 not 500; empty tenant returns `UpToDate`. Keep it targeted.
      → **PUSH** `test(sync): /sync/batches returns 200 for populated and empty tenants`

- [ ] **T7 — Sync coherence review (Task 2 of the request).** Purposeful, targeted review (NO
      wasteful scanning). Confirm/record that client↔server are standard & coherent:
      - Contract version header `2` both sides ✔ (already confirmed).
      - Push envelope fields vs server `CommandEnvelopeValidator` + `CommandPayloadCatalog`.
      - Pull request/response records (`OpenBatchRequest`/`OpenBatchResponse`) match
        `SyncFeedClient`/`OpenBatchOutcome` mapping.
      - Identity rebind at push (`HttpCommandTransport.RebindIdentityAsync`) still correct after T1.
      Fix any concrete mismatch found; if none, note it. → **PUSH** if any change; else no push.

- [ ] **T8 — Open/verify PR.** Ensure branch `genspark_ai_developer` is pushed and a PR to `main`
      exists/updated with a summary of BUG 1 + BUG 2 fixes. Share PR URL with the user.
      (`scripts/git/push.sh` already syncs/pushes; create PR via gh/API if needed.)

- [ ] **T9 — FINAL (last task): delete this plan file only.**
      `rm Plan/sync-fix-plan.md` (do NOT touch any other file, roadmap, or `.gitkeep`).
      → **PUSH** `chore(plan): remove completed sync-fix plan`

---

## Guardrails (from the request, keep in mind every task)
- Targeted file/code review only — no aimless scanning.
- Standard, high-quality fixes. No full-solution build / no long test runs.
- Any heavy/large fix or sizable code written mid-task → push it immediately with a clear message.
- Only `Plan/sync-fix-plan.md` is deleted at the very end (T9); nothing else.
- PUSH after every completed task via `scripts/git/push.sh` (LOCKED).
