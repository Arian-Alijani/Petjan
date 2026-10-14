# PLAN — Offline-Sync + Clinic Isolation + Logout Button

> Branch: `genspark_ai_developer` · Push rule: after EVERY task run `scripts/git/push.sh "msg"` (code=3 ⇒ call `setup_github_environment` tool, re-run). No push = fail.
> Build/test dotnet: `export PATH="$HOME/.dotnet:$PATH"` first. If `dotnet` missing, run `bash scripts/sandbox/install-prereqs.sh` once (~5min). NO full-solution build/test — build/test ONLY touched projects.
> App = offline-first vet-clinic desktop (Avalonia MVVM, C#/.NET 10). Client = SQLite replica; Server = PostgreSQL. Two sync pipelines: (A) command-outbox PUSH, (B) state-sync PULL. One clinic = one tenant.

---

## ARCHITECTURE FACTS (verified — do NOT re-investigate)

### Local store = single SQLite DB, one tenant at a time
- Path `LocalStoreOptions.DatabasePath`; bound to ONE tenant via `LocalInstallation` row.
- `src/Animora.Persistence.Sqlite/Context/LocalInstallationState.cs`: `DefaultTenantId = "T-LOCAL-DEFAULT"`; `TenantId => _installation?.TenantId ?? DefaultTenantId`; holds `SemaphoreSlim TransitionGate`.
- Every local entity carries `TenantId`; EF global query filter pins reads to `CurrentTenantId`. So "showing another clinic's data" means the LOCAL DB still physically holds clinic-A rows AND the active `TenantId` still equals A's.

### Tenant isolation mechanism EXISTS and is correct in isolation
`src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs → EnsureBoundToAsync(tenantId, ct)`:
- Rejects `tenantId == DefaultTenantId` (returns `ValidationFailed`).
- Takes `TransitionGate`. If already bound to same tenant → just `state.Set` + Ok.
- Else: refuses if unsettled `PendingCommand` exist (`SyncPendingChanges`), then in ONE transaction DELETEs ALL relational tables except `LocalInstallation` + `Permission`, `installation.BindTenant(tenantId)`, re-seeds only identity ref data (Tenant default + RoleDefinitions) for the NEW tenant, resets appointment-settings sidecar, commits, `state.Set`, clears `SessionContextCache`, invalidates timezone. Secure-delete ON. This is the standard, correct wipe-and-rebind.

### Login flow (desktop) — `src/Animora.Desktop/Pages/Login/LoginViewModel.cs`
`SignInAsync` (L206): (1) `EstablishServerSessionAsync` (online-first, mandatory if server configured) → (2) `SignInLocalAsync`.
`SignInLocalAsync` (L252):
- **L258-266 FRESH-INSTALL / EMPTY-STORE BRANCH**: if `IsFirstRunAsync()==true` AND server says Manager → `CompleteFirstRunAsync(FirstRunRequest(clinic|user, ...))`. ⚠️ This runs `CompleteFirstRunSetupCommand` which seeds the LOCAL store under **`DefaultTenantId` (`T-LOCAL-DEFAULT`)**, NOT the server's real `TenantId`. See "ROOT CAUSE" below.
- Else `SignInAsync` local; on `CREDENTIALS_INVALID` while online-ok → `EnsureLocalMirrorAsync` then retry.

### Online login — `src/Animora.Client.Services/Identity/OnlineAuthClient.cs → LoginAsync` (L29)
Correct order: register device → reject revoked/pending → `sessions.ClearAsync` → **`EnsureBoundToAsync(login.TenantId)`** (L72-77) → write session → mirror capability/seal licence/write offline verifier. So the ONLINE path DOES isolate when `login.TenantId` differs from current.

### Offline login — `src/Animora.Client.Services/Identity/OfflineSessionAuthorizer.cs → AuthorizeAsync` (L28)
Credential mirror stores exactly ONE tenant (`m.TenantId`). **L45**: `if (state.TenantId != m.TenantId) return Unavailable("wrong_local_tenant")`. Then `EnsureBoundToAsync(m.TenantId)`. ⚠️ Offline, only the LAST logged-in clinic's verifier exists → a second clinic can't start offline (expected), but see Task-2 edge cases.

### Sign-out — `src/Animora.Client.Services/Identity/AuthenticationService.cs → SignOutAsync` (L45)
Ends local session, clears capability gate, sets `SessionSnapshot.SignedOut`, calls `online.LogoutAsync`. **Comment L59: offline verifiers for the bound clinic are INTENTIONALLY retained; local DB is NOT wiped on sign-out.** Isolation is instead enforced on the NEXT login via `EnsureBoundToAsync`.
`src/Animora.Desktop/App.axaml.cs → OnProfileSignedOut` (L189): fully REBUILDS the DI container (`Services = ConfigureServices()`), disposes the old one, returns to login window. Comment L187: this discards all singleton pages/in-memory rows so the next clinic doesn't leak via memory. So after sign-out, memory is clean; only the on-disk DB still holds the old tenant until next login rebinds.

### Logout button — ALREADY IMPLEMENTED (verify only)
- `ProfilePageViewModel.cs` L31-47: `[RelayCommand] SignOutAsync` → `_authentication.SignOutAsync("user_requested")` → raises `SignedOut`. `IsSigningOut` flag + toast on failure.
- `ProfilePageView.axaml` L391: `<Button Classes="outline" Content="خروج از حساب" Command="{Binding SignOutCommand}" .../>`.
- `App.axaml.cs` L165: `ProfilePageViewModel.SignedOut += OnProfileSignedOut` wired on login; L189 handler returns to login.
- NOTE L1008 `IsVisible="{Binding CanSignOut}"` + `EndSessionCommand` belong to the per-SESSION row (`ProfileSessionViewModel.CanSignOut`, `ProfileItemViewModels.cs` L115) — a DIFFERENT feature (remote session mgmt, currently a toast stub `EndSession` L889). Do not confuse with account logout.

---

## TASK 1 — Offline-added data syncs correctly on reconnect
**Goal:** records created while offline (across all app sections) PUSH to server and re-appear consistently; no silent loss/tombstone.

**Context already fixed (previous session, in git history — keep intact):**
- `8879f80c fix(server): stamp UpdatedAt on save so synced rows are not tombstoned` + `RecordProjector` maps `-infinity`→null. Root server bug was `TenantStampingInterceptor` not attached to `AnimoraServerDbContext` (interceptor wiring) and `AccountingPeriod` not provisioned. If those commits are present on `main`, server side is OK.

**What to verify (targeted, no full build):**
1. Confirm server interceptor-wiring + AccountingPeriod-provisioning fixes are present:
   - `grep -n "AddInterceptors" src/Animora.Persistence.PostgreSql/DependencyInjection/PersistenceServiceCollectionExtensions.cs` → must attach `sp.GetServices<IInterceptor>()` on `AddDbContext<AnimoraServerDbContext>`.
   - `grep -n "AccountingPeriod" src/Animora.Persistence.PostgreSql/Identity/TenantProvisioner.cs` → must seed a period on tenant create.
   - If MISSING, re-apply (details already proven in the uploaded report §5: add `.AddInterceptors(sp.GetServices<IInterceptor>()).ConfigureWarnings(w=>w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))`; seed period via `ServerPeriodCalendar.MonthContaining(clock.UtcNow, zone)` + `AccountingPeriod.Create`).
2. Client PUSH pipeline (command-outbox): offline-created commands land in `PendingCommand` (status != Settled), drained on reconnect by the command transport (`src/Animora.Sync.Custom/HttpCommandTransport.cs`, `SettlementReconciler.cs`, `CommandIdRemapService.cs`). Verify: a `PendingCommand` with a client-generated id is remapped to the server id on settlement and not re-sent. Check `SettlementReconciler` marks `Settled`/`Reversed` correctly.
3. Client PULL pipeline: `SyncOrchestrator.PullOnceAsync` (`src/Animora.Client.Services/Sync/SyncOrchestrator.cs`) gated by `TransitionGate` (L34) and only runs with an authenticated session for the SAME tenant (L108 `state.TenantId == session.TenantId`). `ReplicaMaterializer` upserts/tombstones from wire records.
4. **Cross-section coverage**: the wipe/sync is schema-driven (iterates `db.Model.GetRelationalModel().Tables`), so every entity is covered automatically. The risk area is any entity NOT carrying a proper `TenantId`/`UpdatedAt` cursor, or any client command that writes directly WITHOUT going through the outbox (would never push). Scan: `grep -rln "SaveChangesAsync" src/Animora.Client.Services` and confirm writes go through the dispatcher/outbox, not ad-hoc.

**Decision gate:** If verification shows PUSH+PULL already correct for offline adds (likely, given prior fixes), Task 1 = add/confirm a focused regression test only (no heavy work). If a gap is found (an entity not pushed, or a cursor not stamped), fix it minimally + test.

**Status:** ☐ not started

---

## TASK 2 — CRITICAL: Clinic A data shown after logging into clinic B (security)
**Symptom:** login A → logout → login B still shows A's data. Must be fully controlled, standard, secure (not a hack).

### ROOT CAUSE (primary hypothesis — verified by reading the code path)
The fresh-install / empty-store branch in `LoginViewModel.SignInLocalAsync` (L258-266) seeds the local store under **`DefaultTenantId` (`T-LOCAL-DEFAULT`)** via `CompleteFirstRunAsync`, because it is reached whenever `IsFirstRunAsync()==true`. Meanwhile `OnlineAuthClient.LoginAsync` calls `EnsureBoundToAsync(login.TenantId)` BEFORE the local sign-in — BUT when the local store is empty/first-run, the data the user then creates may be written while the installation is still effectively `DefaultTenantId`, OR the first-run seeder rebinds/creates a default-tenant clinic that subsequent `EnsureBoundToAsync` no longer wipes because:
- `EnsureBoundToAsync` early-returns Ok WITHOUT wiping when `installation.TenantId == tenantId` (L68-72). If a prior login already bound the installation to clinic A's real TenantId and clinic A rows exist, then logging into clinic B (different TenantId) SHOULD wipe. **So the pure online→online A→B case is handled.**

**Therefore the real leak is one of these edge cases — confirm which by reproducing/reading:**
1. **First-run binds to DefaultTenant, not server tenant.** Inspect `CompleteFirstRunSetupCommand` handler (`grep -rln "CompleteFirstRunSetupCommand" src`) and `LocalSeeder.cs`: does first-run write rows under `DefaultTenantId` or under the server's real tenant? If `DefaultTenantId`, then after first clinic the installation is stuck at a tenant that never matches the next `login.TenantId`, OR the seeded default rows survive because the server tenant differs and the wipe runs but default-tenant rows were already filtered out. Pin down exactly.
2. **Offline fallback to the wrong clinic.** `OnlineAuthClient.LoginAsync` L38-43: if the server is unreachable/transient, it falls back to `OfflineSessionAuthorizer` with the typed `tenantCode`. The offline authorizer checks `state.TenantId != m.TenantId` (A's mirror) — so logging into B OFFLINE is refused (`wrong_local_tenant`) and the user stays on A's shell/data. Confirm the UI does NOT proceed to the shell on this refusal.
3. **Sign-out does not clear the SESSION/UI tenant, and the next login path short-circuits the rebind.** Because `SignOutAsync` retains the bound clinic and does NOT wipe, if the next login somehow does NOT call `EnsureBoundToAsync` (e.g., offline path with matching mirror, or a code path that skips it), B's shell opens on A's still-present rows.

### REQUIRED FIX APPROACH (standard, secure)
- **Enforce a single invariant:** a shell is shown for tenant T ⟺ the local installation is bound to T AND all rows for any tenant ≠ T have been physically removed. Centralize this so EVERY successful login (online OR offline, first-run OR returning) passes through `EnsureBoundToAsync(resolvedServerTenantId)` with the REAL server tenant id, never `DefaultTenantId`.
- **Fix first-run binding:** `CompleteFirstRunAsync`/`CompleteFirstRunSetupCommand` on a server-backed login MUST provision the local store under the server's `TenantId` (available from `_onlineOutcome.TenantId`), not `DefaultTenantId`. If the command cannot take a tenant id, call `EnsureBoundToAsync(online.TenantId)` immediately after first-run completes so the seeded default-tenant rows are wiped and rebound. (Simplest correct option: after any successful `SignInLocalAsync`, assert `state.TenantId == online.TenantId`; if not, it's a bug — rebind.)
- **Guarantee rebind on returning login too:** the online `LoginAsync` already rebinds; ensure the offline path and the first-run path reach the same guarantee. Do NOT special-case data clearing in the UI — keep it in the persistence/maintenance layer (`EnsureBoundToAsync`).
- **Security:** rebind must run BEFORE any session token is published and BEFORE the shell opens (online path already does L68-77). Ensure no window where B's shell reads A's rows. Secure-delete already ON in the wipe.
- Preserve the existing `TransitionGate` serialization and the `PendingCommand`-unsettled guard (don't silently discard a user's unsynced work when switching — surface `SyncPendingChanges`).

### VERIFY
- Read `CompleteFirstRunSetupCommand` handler + `LocalSeeder.cs` to confirm the tenant id used at first-run (THE decisive check).
- Read `EstablishServerSessionAsync` in `LoginViewModel.cs` (above L206) to see exactly what it stores in `_onlineOutcome` and whether it proceeds to shell on offline refusal.
- Add a regression test asserting: after binding to A then logging in as B, (a) `state.TenantId == B`, (b) zero rows remain with `TenantId == A`, (c) query of a seeded A entity returns 0. Test lives client-side (`tests/Animora.Persistence.Tests/`) over a real SQLite temp DB — pattern exists in that project.

**Status:** ☐ not started

---

## TASK 3 — Logout button in Settings (ALREADY IMPLEMENTED — verify)
Button + command + shell wiring all present (see ARCHITECTURE FACTS → "Logout button").
**Do only:** confirm the button is placed in a visible/sensible Settings location (currently `ProfilePageView.axaml` L391 — check which tab/panel it sits in; the account-level logout should be reachable, e.g. overview or security tab). If it is only in an obscure spot or not reachable, move/duplicate it to a clear place. Confirm `SignOutCommand` exists (it does) and `IsSigningOut` disables the button while running (add `IsEnabled`/busy binding if missing). No new logout logic needed — reuse `SignOutAsync`.
**Status:** ☐ not started

---

## EXECUTION ORDER
1. Task 2 verify-root-cause (read `CompleteFirstRunSetupCommand` + `LocalSeeder` + `EstablishServerSessionAsync`) → confirm which edge case → implement minimal standard fix in persistence/auth layer → regression test → build touched projects → **push**.
2. Task 1 verify server fixes present + client push/pull path → fix only if gap → test → **push**.
3. Task 3 verify button placement/binding → adjust if needed → **push**.
4. FINAL: delete `/home/user/uploaded_files/pasted-text-1792000863779.txt` and delete this plan file → **push**.

## PUSH CHEATSHEET
```
scripts/git/push.sh "fix(auth): rebind local store to server tenant on first-run login (clinic isolation)"
```
If exit 3 → call `setup_github_environment` tool → re-run same command. If exit 2 (rebase conflict) → resolve preferring remote → re-run.

## PROGRESS LOG
- (none yet)
