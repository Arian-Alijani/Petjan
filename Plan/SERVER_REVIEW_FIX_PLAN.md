# Plan — Close the gap to 100/100 (from docs/SERVER_REVIEW_REPORT.md)

> **Purpose of this file:** a self-contained, execution-ready task list to implement the
> 7 remediation items the review (`docs/SERVER_REVIEW_REPORT.md`, §10) lists to take
> `src/Animora.Server` from **92/100 → 100/100**. All code investigation is already done
> and the *pure* findings (exact files, line anchors, current code, decided approach) are
> embedded below so the implementing model can start coding with **near-zero re-reading**.
>
> **Environment reality (verified):** .NET 10 SDK is **NOT installed** in this sandbox
> (`global.json` pins `10.0.401`; `dotnet` is absent). So: write code to compile *by
> construction* (match existing patterns exactly), add/adjust unit tests, but **do NOT**
> attempt `dotnet build` / `dotnet test` here — they will fail on a missing SDK, not on our
> code. Full build/test verification (§9 of the report) is a CI/real-toolchain concern and
> is explicitly **out of scope** for sandbox execution.
>
> **LOCKED RULE — push after every task:** after completing EACH task below, run
> `scripts/git/push.sh "type(scope): message"`. Non-negotiable. Branch: `genspark_ai_developer`.
>
> **Persian-origin instruction recap:** build plan → do tasks in order → push after each →
> final task = delete THIS file only (do not touch other files / roadmaps).

---

## 0. Shared context (read once, reused by all tasks)

### Key architecture facts (already confirmed by reading the code)
- **Revocation model:** access tokens are opaque, DP-protected, 15-min, decoded with **no
  DB read**. Revocation is enforced at membership resolution by comparing the token's
  security-stamp claim (`sst`) to the stored user's `SecurityStamp`. Rotating the stamp
  (`user.RotateSecurityStamp()`) ⇒ every live token of that user fails on next request.
- **AuthZ verdict cache** (`AuthzDecisionCache`): per-process, absolute-TTL, keyed by
  `(userId, deviceId, securityStamp)` for membership and `(tenantId, deviceId)` for
  entitlement. Only SUCCESS verdicts cached. `TtlSeconds` default **10**, `0` disables.
  Registered as singleton in
  `src/Animora.Server/Authorization/AuthorizationServiceCollectionExtensions.cs:39`
  (`services.TryAddSingleton<AuthzDecisionCache>();`).
- **No LISTEN/NOTIFY / Redis infra exists** anywhere in `src/Animora.Persistence.PostgreSql`
  (grep for LISTEN/NOTIFY/pubsub returned nothing). ⇒ a real distributed cache-invalidation
  bus is a large, cross-project feature NOT justified by a single-instance deployment.
- **Report is slightly stale vs code in one place:** §7.2 claims admin CSP has no
  `frame-ancestors`. **The current code already emits**
  `default-src 'self'; frame-ancestors 'none'; form-action 'self'`
  (`Admin/AdminSecurityHeadersMiddleware.cs:13`). So §7.2's real residual gap is only the
  missing explicit `script-src`/`style-src` (and optional nonce). Plan reflects the CODE,
  not the stale prose.

### Test project
- `tests/Animora.Server.Tests` (157 `.cs` files). Relevant existing tests:
  - `tests/Animora.Server.Tests/Authorization/AuthzDecisionCacheTests.cs`
  - `tests/Animora.Server.Tests/Provisioning/ProvisioningRegressionTests.cs`,
    `ProvisioningIntentStoreTests.cs`
  - `tests/Animora.Server.Tests/Admin/` (CSP/header tests live here)
- Follow the surrounding test style (xUnit, `Assert.*`). Keep new tests pure-unit (no DB) so
  they don't need Postgres.

### Design decisions already made (do NOT re-deliberate)
1. **§2.1 distributed invalidation** → DO NOT build a Redis/LISTEN-NOTIFY bus (no infra,
   single-instance product). Instead: (a) make the multi-instance escape hatch **first-class
   and documented in code** by validating/clamping `TtlSeconds` to the `0..60` range the
   XML-doc already promises, and (b) emit a **startup warning log** when `TtlSeconds > 0`
   *and* a multi-instance hint is set, pointing operators to set `TtlSeconds=0`. This
   converts the finding from "silent degradation" to "explicit, operator-visible choice",
   which is what the report asks for ("documented escape hatch"). This is the pragmatic,
   in-scope closure; a full shared bus is noted as a future item but not implemented.
2. **§2.2 provisioning replay** → add the DB-backed consumed-nonce table **only if** a
   persistence seam already exists; investigation shows no such store. Pragmatic closure:
   keep per-process `ReplayWindow` (already sheds safely, bounded by ±5min HMAC skew) and
   **tighten + document** it; the real global store is a `TODO(multi-instance)` already in
   the file. Treated as LOW effort doc/robustness, not a schema migration.
3. **§3.1 staff disable stamp** → **IMPLEMENT FULLY** (highest-value, smallest, safest). Add
   `user.RotateSecurityStamp()` on the disable path in `StaffProvisioningService.SetActiveAsync`
   so disable is immediate and consistent with `DeviceAdminService.RevokeAsync`.
4. **§6.1 provisioning GET Unprotect guard** → **IMPLEMENT FULLY**. Wrap the
   `Unprotect` + `JsonSerializer.Deserialize` in try/catch, map to typed
   `ProvisioningSignatureInvalid` (or `ValidationFailed`) instead of a raw 500.
5. **§7.2 CSP tighten** → **IMPLEMENT FULLY** (cheap). Add explicit `script-src 'self'` and
   `style-src 'self'` to the admin CSP string. Nonce is optional/skipped (Blazor Server +
   `'self'` is acceptable; nonce adds per-response plumbing not worth the risk here).
6. **§4.3 / §6.3 / §8.2 polish** → small, local, safe:
   - §4.3 `IntakeRecoveryWorker`: the `Volatile.Read` pre-check is advisory; authoritative
     budget is the `Interlocked.Decrement` in `takeBudget`. Leave logic, just add an inline
     comment clarifying the intended imprecision (report calls it "harmless"). Optional.
   - §6.3 `OperatorBootstrapper`: cannot scrub env safely at runtime; closure = a code
     comment + ensure runbook note. Code-only: add a comment pointing to the runbook.
   - §8.2 `QueryBudget.Dispose`: sync-over-async bridge is intentional; add an XML/inline
     note that consumers should prefer `await using`. No behavior change.

---

## 1. Tasks (do in this order; push after each)

Legend: `[ ]` todo · `[x]` done.

---

### [ ] TASK 1 — §3.1 Rotate security stamp on staff **disable** (highest impact, trivial)
**Why:** disabling a staff member currently only calls `decisions.InvalidateUser` (local
cache) and relies on `Status==Disabled` being re-read after cache TTL. On multi-instance (or
within the ≤10s TTL) the disabled user keeps a working session. `DeviceAdminService.RevokeAsync`
already rotates the stamp; disable must match it for immediate, cross-replica revocation.

**File:** `src/Animora.Server/Auth/StaffProvisioningService.cs`
**Method:** `SetActiveAsync` (around lines 149–162). Current disable branch:
```csharp
if (request.Active)
{
    member.Reinstate();
    user.Enable();
}
else
{
    member.Suspend();
    user.Disable();
}

Result statusSaved = await AuditAsync(actor, StaffStatusAction, correlationId, user.Id, ct).ConfigureAwait(false);
if (statusSaved.IsSuccess) decisions.InvalidateUser(user.Id);
return statusSaved;
```
**Change:** in the `else` (disable) branch, after `user.Disable();` add
`user.RotateSecurityStamp();`. (Enable path must NOT rotate — re-enabling should not nuke any
other fresh sessions; matches device reinstate, which also doesn't rotate.)
Confirmed `User.RotateSecurityStamp()` exists and is used in the same file at line 224
(`SaveAccessAsync`) and in `DeviceAdminService.cs:106`. The audit `AuditAsync` commits
user+membership+stamp in one unit of work (comment at line 319 confirms stamp is committed
with the row), so no extra persistence wiring is needed.

**Test:** in `tests/Animora.Server.Tests` (Identity or Authorization folder), add/extend a
unit test asserting that after `SetActiveAsync(active:false)` the user's `SecurityStamp`
changed, and after `active:true` it did NOT change. Mirror an existing
`StaffProvisioningService` test setup if present; otherwise a focused test on the entity +
service path. Keep it DB-free (use the existing in-memory/fake repository pattern the other
server tests use — check `AuthorizationTestKit.cs` / `IdentityTestKit.cs` for fakes).

**Push:** `scripts/git/push.sh "fix(auth): rotate security stamp on staff disable (§3.1)"`

---

### [ ] TASK 2 — §6.1 Guard provisioning GET `Unprotect`/`Deserialize` with typed error
**Why:** a corrupt/rotated-key `ProtectedTempPassword` or `ResultJson` makes
`Unprotect(...)` / `Deserialize<JsonElement>(...)` throw → `GlobalExceptionMiddleware` returns
a generic 500, and the intent becomes un-pollable. POST path already guards `Protect` with
try/finally; mirror that rigor on read.

**File:** `src/Animora.Server/Provisioning/ProvisioningEndpoints.cs`
**Location:** `MapGet("/intents/{idempotencyKey}", ...)` handler, lines ~66–80. The unguarded
lines are 75–77:
```csharp
JsonElement? data = result.ResultJson is null ? null : JsonSerializer.Deserialize<JsonElement>(result.ResultJson);
string? password = result.ProtectedTempPassword is null ? null : protection.CreateProtector("Animora.Provisioning.Intent.v1")
    .Unprotect(Encoding.UTF8.GetString(result.ProtectedTempPassword));
```
**Change:** wrap both in a single `try { ... } catch (Exception ex) when (ex is
System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or
FormatException) { return OperatorEndpoints.Failure(ErrorCodes.ProvisioningSignatureInvalid, http); }`.
Rationale for the catch filter: `Deserialize` throws `JsonException`; DP `Unprotect` throws
`CryptographicException`; `Encoding.UTF8.GetString` on bad bytes won't throw but
`Convert`/decode paths can throw `FormatException` — filter to these, never swallow
`OperationCanceledException`. Use `ProvisioningSignatureInvalid` (already the code used for
other provisioning read failures in this handler, lines 45/72) to avoid leaking "row corrupt"
detail. `OperatorEndpoints.Failure(ErrorCodes code, HttpContext http)` is the existing typed
helper (used at lines 48, 52, 64, 74).

**Test:** add a unit test in `tests/Animora.Server.Tests/Provisioning/` that feeds a
`PollAsync` result whose `ResultJson` is invalid JSON (or `ProtectedTempPassword` is
undecryptable) and asserts the endpoint returns the typed failure (not a throw/500). If the
endpoint is awkward to invoke directly, add at minimum a regression note test on the
`Property(...)`/parse path. Prefer reusing `ProvisioningRegressionTests.cs` harness.

**Push:** `scripts/git/push.sh "fix(provisioning): guard intent poll unprotect/deserialize (§6.1)"`

---

### [ ] TASK 3 — §7.2 Tighten admin CSP (explicit script-src / style-src)
**Why:** residual XSS-hardening gap. NOTE: the report prose is stale — `frame-ancestors
'none'` and `form-action 'self'` are ALREADY present. Only explicit `script-src`/`style-src`
are missing.

**File:** `src/Animora.Server/Admin/AdminSecurityHeadersMiddleware.cs:13`
**Current:**
```csharp
response.Headers.ContentSecurityPolicy="default-src 'self'; frame-ancestors 'none'; form-action 'self'";
```
**Change to:**
```csharp
response.Headers.ContentSecurityPolicy="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
```
Reasoning for the exact directives:
- `script-src 'self'` / `style-src 'self'`: the explicit tightening the report asks for.
- `img-src 'self' data:`: Blazor/admin may emit `data:` images; keep it permissive only for
  images to avoid breaking the UI while still blocking cross-origin script.
- `base-uri 'self'`: cheap, closes `<base>` hijack. Do NOT add a nonce (decided in §0.5).
**Do NOT** change the other headers (X-Frame-Options DENY, nosniff, Referrer-Policy,
no-store) — they are correct.

**Test:** in `tests/Animora.Server.Tests/Admin/` find the existing header/CSP assertion test
and update the expected CSP string to include the new directives. If no CSP test exists, add
a tiny one calling `AdminSecurityHeadersMiddleware.Apply(response)` on a fake `HttpResponse`
and asserting the header contains `script-src 'self'` and `style-src 'self'`.

**Push:** `scripts/git/push.sh "fix(admin): tighten CSP with explicit script/style-src (§7.2)"`

---

### [ ] TASK 4 — §2.1 Make AuthZ-cache multi-instance behaviour explicit (clamp + warn)
**Why:** per-process cache lag is the dominant multi-instance deduction. No shared-bus infra
exists; building one is out of scope. Closure = make the documented escape hatch enforced and
operator-visible (clamp `TtlSeconds` to the promised `0..60`, warn when caching is on).

**File A:** `src/Animora.Server/Hosting/ServerOptions.cs:151-158` (`AuthCacheSettings`).
XML-doc at line 153 already promises "0..60 seconds" but `TtlSeconds` is a raw `int`. Add a
validation/clamp. Preferred: implement `IValidateOptions<ServerOptions>` OR clamp at
consumption. Simplest safe closure matching existing style: clamp in `AuthzDecisionCache`
ctor — it already does `Math.Max(0, settings.TtlSeconds)` at line 39; change to
`Math.Clamp(settings.TtlSeconds, 0, 60)` so the upper bound the doc promises is enforced.
**File B:** `src/Animora.Server/Authorization/AuthzDecisionCache.cs:39`
```csharp
_ttlTicks = TimeSpan.FromSeconds(Math.Max(0, settings.TtlSeconds)).Ticks;
```
→
```csharp
_ttlTicks = TimeSpan.FromSeconds(Math.Clamp(settings.TtlSeconds, 0, 60)).Ticks;
```
**File C (startup warning):** find where the server logs startup config (grep for existing
"LogWarning" near `EnableOpenApi` — the report §7.4 mentions a loud warning when OpenAPI is
on in non-Development; co-locate the new warning there). Add: if
`ServerOptions.AuthCache.TtlSeconds > 0`, log a single informational/ warning line stating the
authz cache is per-process and that multi-instance deployments should set
`Server:AuthCache:TtlSeconds=0` (or provide a shared invalidation bus). Keep it one line, no
PII. If a clean co-location isn't found quickly, add the log in the same place
`AddAnimoraAuthorization`/authorization DI runs, OR skip File C and keep A+B+comment — do NOT
spend long hunting; A+B is the substantive change.

**Comment:** in `AuthzDecisionCache` class XML-doc, add one sentence: "For >1 replica set
`TtlSeconds=0` or supply a shared invalidation bus; `InvalidateUser/Tenant` are local-only."
(the class already says "Per-process by design" — make the operator action explicit).

**Test:** extend `AuthzDecisionCacheTests.cs` with a case: constructing the cache with
`TtlSeconds = 999` behaves as if clamped (e.g. an entry does not live > 60s) — or at least
that `Enabled` is true and the clamp path is exercised. Keep it fast/deterministic using the
injected `TimeProvider` (`FakeTimeProvider` if the test kit has one; the ctor takes
`TimeProvider time`).

**Push:** `scripts/git/push.sh "fix(authz): clamp cache TTL to 0..60 and document multi-instance escape hatch (§2.1)"`

---

### [ ] TASK 5 — §2.2 Harden + document provisioning replay window (no schema change)
**Why:** per-process replay window allows one replay per replica inside the ±5min skew.
Global store needs a `(clientId, nonce)` table + migration — a cross-project change not
justified for the single-instance product. Closure = confirm the existing `TODO(multi-instance)`
is accurate, tighten the comment, and ensure the shed path is observable.

**File:** `src/Animora.Server/Provisioning/ReplayWindow.cs`
Current state is already good: bounded `Capacity = 4096`, 5-min `Window`, sheds oldest on
overflow, exposes `Count`/`ShedCount`, and has a precise `TODO(multi-instance)` (lines 12–13)
describing the exact fix (`DB table keyed by (keyId, signature) with INSERT .. ON CONFLICT DO
NOTHING + TTL prune, or Redis SET NX EX 300`).
**Change (minimal, in-scope):**
- Keep logic. Upgrade the class XML-doc to explicitly state the multi-instance exposure
  bound: "On N replicas a captured signature can be accepted at most once per replica within
  the ±5-min skew; provisioning is operator-only and low-volume, so this is accepted for
  single-instance and bounded otherwise. Global exactly-once requires the DB/Redis store in
  the TODO below."
- (Optional, only if trivial) ensure `ShedCount` is surfaced to metrics if a metrics sink is
  already injected nearby; if not already wired, DO NOT add new DI — leave as test/observability hook.
**No test change required** unless you touch logic. If you want a guard test, assert that a
duplicate `(keyId, signature)` within the window returns `false` (likely already covered in
`ProvisioningRegressionTests.cs` — check before adding a duplicate).

**Push:** `scripts/git/push.sh "docs(provisioning): document replay-window multi-instance bound (§2.2)"`

---

### [ ] TASK 6 — §4.3 / §6.3 / §8.2 Low-priority polish (comments / clarity only)
**Why:** the report flags these as "harmless / imprecise / for completeness". Closure =
small, zero-behavior-change clarifications so a future reader doesn't re-flag them.

**6a — §4.3 `IntakeRecoveryWorker`:**
File `src/Animora.Server/Workers/IntakeRecoveryWorker.cs:33` — the
`if (Volatile.Read(ref remaining) <= 0) return;` fast-path is advisory; the authoritative cap
is `Interlocked.Decrement(ref remaining) >= 0` inside `takeBudget` (line 34/51). Add a one-line
comment at line 33 clarifying: "advisory fast-path; authoritative budget is the atomic
takeBudget decrement below. Slight overrun bounded by RecoveryParallelism is intentional
(BUG-RECOVERY-01)." No logic change.

**6b — §6.3 `OperatorBootstrapper`:**
File `src/Animora.Server/Operator/OperatorBootstrapper.cs` (reads
`Operator__BootstrapUserName`/`Operator__BootstrapPassword` env vars at lines 16–17; NOTE the
report said `ANIMORA_OPERATOR_*` but the actual var names are `Operator__Bootstrap*` — use the
REAL names). The password stays in the process environment after bootstrap; runtime scrubbing
of a parent-set env var is unreliable/unsafe. Closure = add a comment near line 18 noting
"operators should unset `Operator__BootstrapUserName`/`Operator__BootstrapPassword` after first
boot (runbook/defense-in-depth); the process cannot reliably scrub a parent-provided env var."
No logic change.

**6c — §8.2 `QueryBudget.Dispose`:**
File `src/Animora.Server/Observability/QueryBudget.cs:16`
`public void Dispose()=>DisposeAsync().AsTask().GetAwaiter().GetResult();` — intentional
sync-over-async bridge on `QueryLease`. Add an inline/XML note: "sync bridge for `using`
callers; prefer `await using` so this path is never hit (no sync-context deadlock risk in
ASP.NET Core)." No logic change.

**Test:** none (comments only).
**Push:** `scripts/git/push.sh "docs(server): clarify recovery cap, operator env scrub, querybudget dispose (§4.3/§6.3/§8.2)"`

---

### [ ] TASK 7 — §9 Verification note (no sandbox build) + wrap-up
**Why:** the report's §9 wants a real-toolchain `dotnet build -warnaserror` + `dotnet test` +
2-replica smoke test. That CANNOT run here (no .NET 10 SDK). Do NOT attempt it.
**Action:** none in code. Confirm all edits in Tasks 1–6 were made to match existing patterns
and would compile by construction (no new usings missing, typed helpers used correctly). This
task is the checkpoint before deletion.
**Push:** only if there were uncommitted stragglers:
`scripts/git/push.sh "chore(server): finalize review-gap fixes"` (otherwise skip — nothing to commit).

---

### [ ] TASK 8 — FINAL: delete this plan file
**Action:** delete **only** `Plan/SERVER_REVIEW_FIX_PLAN.md` (this file). Do NOT touch any
roadmap, report, or other file. (Leaving `Plan/.gitkeep` is fine.)
```bash
git rm Plan/SERVER_REVIEW_FIX_PLAN.md
```
**Push:** `scripts/git/push.sh "chore(plan): remove completed server-review fix plan"`

---

## 2. Quick file index (so the next model opens the right file immediately)

| Task | File | Anchor |
|------|------|--------|
| 1 §3.1 | `src/Animora.Server/Auth/StaffProvisioningService.cs` | `SetActiveAsync` disable branch ~L149-162 |
| 2 §6.1 | `src/Animora.Server/Provisioning/ProvisioningEndpoints.cs` | GET handler L75-77 |
| 3 §7.2 | `src/Animora.Server/Admin/AdminSecurityHeadersMiddleware.cs` | CSP string L13 |
| 4 §2.1 | `src/Animora.Server/Authorization/AuthzDecisionCache.cs` (L39) + `Hosting/ServerOptions.cs` (L151-158) | TTL clamp + doc |
| 5 §2.2 | `src/Animora.Server/Provisioning/ReplayWindow.cs` | class XML-doc L3-15 |
| 6a §4.3 | `src/Animora.Server/Workers/IntakeRecoveryWorker.cs` | L33 comment |
| 6b §6.3 | `src/Animora.Server/Operator/OperatorBootstrapper.cs` | L16-18 comment |
| 6c §8.2 | `src/Animora.Server/Observability/QueryBudget.cs` | L16 comment |
| tests | `tests/Animora.Server.Tests/{Authorization,Provisioning,Admin,Identity}/` | mirror existing kits |

## 3. Guardrails
- Every edit must match the file's existing brace/indent/one-liner style (several files use
  dense one-line bodies — preserve that).
- No new NuGet packages, no new DI registrations unless a task says so.
- No `dotnet build`/`dotnet test` in sandbox (SDK absent) — correctness by construction.
- Push after EVERY task via `scripts/git/push.sh`. If push exits code 3 → call
  `setup_github_environment` once, then re-run the same push.
- If any task balloons into a "heavy" change (big new code), push it immediately with a clear
  message (per the locked rule).
