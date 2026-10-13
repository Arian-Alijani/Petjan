# Plan — Client↔Server Integration Audit & Targeted Fixes

> **Self-contained.** A fresh AI session can implement every task below **without re-reading the
> codebase for discovery**. All root causes, exact file paths, line anchors, and fix recipes are
> pre-resolved here. Only open a file to apply the edit, not to re-investigate.
>
> **PUSH RULE (locked):** after completing *each* task below, run `scripts/git/push.sh "msg"`.
> If it exits `3`, call the `setup_github_environment` tool once, then re-run. Never batch tasks
> without a push between them.
>
> **Discipline:** no full build / no long test runs. Targeted edits only. Build a single project
> if you must verify (`dotnet build src/<Project>` on the one you touched), never the whole solution.

---

## 0. Project shape (facts, no need to re-derive)

- .NET 10 SaaS vet-clinic suite. Desktop = **Avalonia MVVM** (`src/Animora.Desktop`). Server =
  ASP.NET minimal APIs (`src/Animora.Server`). Shared wire DTOs in `src/Animora.Contracts.Wire`,
  UI-facing contracts in `src/Animora.Contracts`. Local store = SQLite
  (`src/Animora.Persistence.Sqlite`). Server store = PostgreSQL (`src/Animora.Persistence.PostgreSql`).
- **The only LIVE client↔server path today** is the online identity surface: login, staff roster
  (`/auth/staff`), device approve/revoke (`/auth/device/{id}/...`), and licensing status
  (`/licensing/status`). The full sync engine (replica materializer etc.) is authored but **not wired
  into the running desktop** — so tasks 1–6 ("integration/sync audit") concentrate on this live
  surface plus data-provenance correctness, not the dormant sync pipeline.
- Dispatch pipeline: `IDispatcher.SendAsync(cmd, CommandContext)` / `.QueryAsync(q, QueryContext)`.
  Both contexts carry identity. Pipeline behaviors live in `src/Animora.Application/Behaviors`.

---

## TASK-7 — History "انجام‌دهنده" always shows «سیستم» (ROOT CAUSE FOUND)  ·  status: ☑ DONE

### Symptom
Every history/activity screen's actor/performer column renders «سیستم» instead of the real user who
did the action.

### Root cause (confirmed, single point)
`src/Animora.Application/Behaviors/CorrelationBehavior.cs` line ~24:
```csharp
using var scope = correlation.Begin(CorrelationIdOf(context));   // ← string overload
```
`CorrelationIdOf` returns **only the CorrelationId string**. The string overload of `Begin`
(`CorrelationContext.Begin(string?)`, `src/Animora.Application/Correlation/CorrelationContext.cs`
~line 22) builds:
```csharp
new AmbientCorrelation(correlationId, string.Empty, string.Empty, string.Empty, null)
//                                    TenantId      UserId        DeviceId      OperationId
```
→ **`AmbientCorrelation.UserId` is always empty.**

Downstream, `ActivityFeedWriter.WriteAsync`
(`src/Animora.Persistence.Sqlite/Activity/ActivityFeedWriter.cs`):
```csharp
var actorId = correlation.Current?.UserId;         // empty
if (string.IsNullOrWhiteSpace(actorId)) actorId = draft.ActorUserId;  // also empty (see below)
```
`ActivityFeedMapper` (`src/Animora.Application/Activity/ActivityFeedMapper.cs`) defaults
`ActorUserId = ""` and never sets it (domain events don't carry a user). So `actorId` ends up empty
→ `ActivityDetailResolver.ResolveActorNameAsync` returns `""` → the history query service
(`src/Animora.Client.Services/History/HistoryQueryService.cs`, `Actor(row)` ~line 70) falls back to
`ActorRef.System` (= `new("system","سیستم",...)`, `src/Animora.Contracts/Identity/ActorRef.cs`).

### Why synced rows are NOT affected (don't touch them)
`src/Animora.Persistence.Sqlite/Activity/SyncedActivityFeed.cs` passes `draft.ActorUserId` explicitly
from the wire record — independent of correlation. Only **locally-authored** events are broken.

### Fix (one file, minimal, safe)
Edit `CorrelationBehavior.Wrap` to seed the FULL ambient correlation from the context instead of just
the id. The contexts are:
- `CommandContext(TenantId, UserId, DeviceId, OperationId, CorrelationId, …)`
  (`src/Animora.Application/Abstractions/CommandContext.cs`)
- `QueryContext(TenantId, UserId, CorrelationId)`
  (`src/Animora.Application/Abstractions/QueryContext.cs`) — no DeviceId/OperationId.

Replace the `using var scope = correlation.Begin(CorrelationIdOf(context));` line with logic that,
when the context is a `CommandContext`/`QueryContext`, calls the **`AmbientCorrelation` overload**:
```csharp
using var scope = correlation.Begin(AmbientFor(context));
...
private static AmbientCorrelation AmbientFor(object context) => context switch
{
    CommandContext c => new AmbientCorrelation(
        string.IsNullOrWhiteSpace(c.CorrelationId) ? IdGenerator.NewId() : c.CorrelationId,
        c.TenantId, c.UserId, c.DeviceId, c.OperationId),
    QueryContext q => new AmbientCorrelation(
        string.IsNullOrWhiteSpace(q.CorrelationId) ? IdGenerator.NewId() : q.CorrelationId,
        q.TenantId, q.UserId, string.Empty, null),
    _ => new AmbientCorrelation(IdGenerator.NewId(), "", "", "", null),
};
```
- `IdGenerator` is in `Animora.Domain.Primitives` (already referenced by `CorrelationContext.cs`;
  add `using Animora.Domain.Primitives;` to the behavior if needed).
- `correlation.CurrentId` (used for the failure `Prefix`) still works — it reads
  `Ambient.Value?.CorrelationId`.
- Keep `CorrelationIdOf` only if still referenced elsewhere; otherwise remove it to avoid a warning.

### Verify (cheap)
`dotnet build src/Animora.Application` — must compile. No test run needed; logic is a pure mapping.
Optional: grep that nothing else relied on the empty-UserId behavior (nothing does).

### Acceptance
Locally-authored activity rows now carry the acting user's id; `ActivityDetailResolver` resolves the
real `User.DisplayName`; history actor column shows the real name. «سیستم» remains only for genuinely
system-origin rows (correlation seeded with a system/empty user, e.g. background jobs) — acceptable.

---

## TASK-9 — Device access: re-enable revoked, block manager self-revoke, verify OS version  ·  status: ☑ DONE

Three sub-items. OS-version (c) is **already done** — just verify. (a) and (b) are new.

### (a) Re-enable a revoked device  (NEW — server + client + UI)
Currently a revoked device is a dead end:
- Domain: `Device.Approve()` (`src/Animora.Domain/Identity/Device.cs`) **throws** for `Revoked`.
- Server: `DeviceAdminService.ApproveAsync` (`src/Animora.Server/Auth/DeviceAdminService.cs`)
  returns `ErrorCodes.DeviceRevoked` for a revoked device.
- UI: `StaffDeviceViewModel.CanRevoke => !IsRevoked`
  (`src/Animora.Desktop/Pages/Profile/ProfileItemViewModels.cs` ~line 434) — a revoked row shows
  only the «لغوشده» label, **no action button**.

**Fix recipe:**
1. **Domain** — add to `Device`:
   ```csharp
   /// <summary>Re-enables a previously revoked device (manager re-authorizes the same installation).</summary>
   public void Reinstate()
   {
       if (Status != DeviceStatus.Revoked) return;      // idempotent for Active/Pending
       Status = DeviceStatus.Active;
       RevokedUtc = null;
       RevocationReason = null;
   }
   ```
   (Place beside `Approve()`/`Revoke()`. Note `DescribeInstallation` early-returns for Revoked, so a
   reinstated device can be re-described afterwards — fine.)
2. **Server service** — add `ReinstateAsync` to `DeviceAdminService` mirroring `ApproveAsync` but:
   - find device; if null → `NotFound`;
   - if `device.Status != DeviceStatus.Revoked` → return `ErrorCodes.ValidationFailed` (or just Ok
     idempotently — prefer returning current status);
   - call `device.Reinstate()`; audit with a new action const
     `public const string DeviceReinstatedAction = "auth.device.reinstated";` then
     `AuditAsync(actor, DeviceReinstatedAction, …, device.Id, ct)`;
   - return `DeviceRegisterResponse(device.Id, device.Status.ToString())`.
3. **Server endpoint** — in `src/Animora.Server/Auth/DeviceEndpoints.cs`, next to the approve/revoke
   maps, add:
   ```csharp
   group.MapPost("/device/{id}/reinstate", ReinstateAsync).RequireAuthorization(PermissionCodes.DevicesManage);
   ```
   and a `ReinstateAsync` handler copied from `ApproveAsync` calling `service.ReinstateAsync(...)`.
4. **Client service** — in `src/Animora.Client.Services/Profile/UserProfileService.cs`, add
   `ReinstateStaffDeviceAsync(string deviceId, CancellationToken ct = default)` mirroring
   `ApproveStaffDeviceAsync` but POSTing to `auth/device/{id}/reinstate`. Add the method to the
   service interface (`IUserProfileService` in `src/Animora.Contracts/Profile/IUserProfileService.cs`).
5. **VM** — in `src/Animora.Desktop/Pages/Profile/ProfilePageViewModel.cs`, add a
   `[RelayCommand] ReinstateDeviceAsync(StaffDeviceViewModel? device)` mirroring
   `ApproveDeviceAsync` but guarded by `device.IsRevoked` and calling
   `_service.ReinstateStaffDeviceAsync(device.DeviceId)` with toast
   `"دسترسی دستگاه بازگردانده شد؛ کاربر می‌تواند دوباره وارد شود."`.
6. **VM model** — add `public bool IsRevoked => …` is already present; expose it for binding (it is a
   public getter already). Nothing else.
7. **XAML** — in `src/Animora.Desktop/Pages/Profile/ProfilePageView.axaml` device card
   (`ItemsControl ItemsSource="{Binding Devices}"`, the `Grid ColumnDefinitions="*,Auto,Auto,Auto"`),
   add a 5th column (change to `*,Auto,Auto,Auto,Auto`) OR reuse the revoke column slot, with a button
   visible only when revoked:
   ```xml
   <Button Grid.Column="4" Classes="primary" Padding="12,6" FontSize="12" Content="اجازهٔ نصب مجدد"
           IsVisible="{Binding IsRevoked}"
           Command="{Binding #Root.((pf:ProfilePageViewModel)DataContext).ReinstateDeviceCommand}"
           CommandParameter="{Binding}" />
   ```

### (b) Manager cannot revoke their own access  (NEW — server guard, authoritative)
`DeviceAdminService.RevokeAsync` has **no self-protection**. Enforce server-side (client UI is a
courtesy only):
- In `RevokeAsync`, after loading `device`, add:
  ```csharp
  if (string.Equals(device.UserId, actor.UserId, StringComparison.Ordinal))
      return Result<DeviceRegisterResponse>.Fail(ErrorCodes.Forbidden);
  ```
  Rationale: revoking any device owned by the acting manager would lock them out; the task is "مدیر
  نمی‌تواند خودش را لغو کند". Comparing on `UserId` (not just the single `actor.DeviceId`) covers all
  of the manager's own devices. `ResolvedMembership` has `UserId` + `DeviceId`
  (`src/Animora.Server/Authorization/IMembershipResolver.cs`).
- **UI courtesy:** the server staff roster marks the caller's own account via `StaffAccountWire.IsProtected`.
  Devices don't carry an "is-mine" flag. Minimal UI option: leave server guard as the enforcement and
  surface the returned `FORBIDDEN` as a clear toast ("نمی‌توانید دسترسی دستگاه خودتان را لغو کنید.").
  The client error map should translate `FORBIDDEN` on this path to that message (see
  `ProfilePageViewModel.ChangeStaffAsync` toast handling — reuse the existing failure toast; optionally
  special-case the code).

### (c) Windows OS version in device name  (ALREADY DONE — verified: ~29 chars « windows · Windows 10.0.26100 » < MaxPlatform=64 on both client and server, no change needed)
- Client captures it: `src/Animora.Client.Services/Identity/DeviceLifecycleService.cs` builds
  `platform = "windows · Windows 10.0.19045"` (family `·` OS description), capped at `MaxPlatform=64`.
- Server stores it via `Device.DescribeInstallation` on the authenticated `register` call
  (login binds placeholders `"unknown"`, register supplies the real values).
- UI renders it: `StaffDeviceViewModel.DescribeSystem` maps family→Persian and keeps the version:
  «سیستم: ویندوز (Windows 10.0.19045)».
- **Verify:** confirm `MaxPlatform = 64` doesn't truncate a real Windows string
  (`"windows · Windows 10.0.26100"` = ~29 chars — safe). No change required unless a longer build
  string is observed; if worried, bump `MaxPlatform` to e.g. 96 in `DeviceLifecycleService` AND the
  server-side `MaxPlatform` cap in `src/Animora.Server/Auth/LoginService.cs` (keep them equal).

### Verify (cheap)
Build the touched projects individually: `dotnet build src/Animora.Domain`,
`dotnet build src/Animora.Server`, `dotnet build src/Animora.Desktop`.

### Acceptance
Revoked device shows an «اجازهٔ نصب مجدد» button that reactivates it; manager cannot revoke a device
owned by themselves (server returns FORBIDDEN, UI shows a clear message); device name still shows the
Windows version.

---

## TASK-8 — Settings: show seat usage "M of N staff accounts" + refresh  ·  status: ☑ DONE

### Facts
- Server already knows the clinic's seat cap: `LicenseStatusResponse.SeatLimit`
  (`src/Animora.Contracts.Wire/Licensing/LicenseContracts.cs`) comes from the tenant's license grant
  (`src/Animora.Server/Licensing/LicenseEndpoints.cs` ~line 38:
  `new LicenseStatusResponse(..., grant?.SeatLimit, grant?.MaxDevices, count, clock.UtcNow)`).
  `SeatLimit` = max accounts the clinic may have; `count`/`ActiveLicenses` = active device licenses
  (NOT the metric we want for *staff accounts*).
- "Used" count of staff accounts = `Staff.Count` already on the client
  (`ProfilePageViewModel.Staff`), and `StaffSummary` already renders counts
  (`src/Animora.Desktop/Pages/Profile/ProfilePageViewModel.cs` ~line 277).
- `ClinicAccountStatus` (`src/Animora.Contracts/Profile/ProfileModels.cs` ~line 196) currently
  **drops** `SeatLimit` — `ServerClinicAccountStatusSource`
  (`src/Animora.Client.Services/Profile/ServerClinicAccountStatusSource.cs`) reads the wire but only
  maps PlanName/expiry.

### Fix recipe (lowest-risk path: reuse existing /licensing/status)
1. **Contract** — add an optional `int? SeatLimit = null` to `ClinicAccountStatus`
   (append at the end of the record's parameter list to avoid breaking positional callers).
2. **Client source** — in `ServerClinicAccountStatusSource.GetAsync`, map `SeatLimit: status.SeatLimit`
   into the returned `ClinicAccountStatus`.
3. **VM** — `ProfilePageViewModel` already loads account status (find where it sets plan/subscription
   from `GetAccountStatusAsync`). Store `SeatLimit` and expose a computed string, e.g.:
   ```csharp
   public string SeatUsageSummary =>
       _seatLimit is int limit
         ? $"{ProfilePresentation.Fa(Staff.Count.ToString())} از {ProfilePresentation.Fa(limit.ToString())} کاربر مجاز"
         : $"{ProfilePresentation.Fa(Staff.Count.ToString())} کاربر";
   ```
   Raise `OnPropertyChanged(nameof(SeatUsageSummary))` wherever `Staff` is reloaded (the
   `ApplyDirectory`/load method that already raises `StaffSummary`, ~line 538) and when account status
   loads.
4. **XAML** — in the staff/add-user card area
   (`src/Animora.Desktop/Pages/Profile/ProfilePageView.axaml`, near `StaffSummary` ~line 1024 or the
   «افزودن کاربر جدید» card ~line 1074), add a `TextBlock Text="{Binding SeatUsageSummary}"`.
5. **Refresh semantics** — "با منقضی کردن نشست‌ها تعدادش رفرش بشه": seat *usage* (staff count) is the
   roster size; it already refreshes on every `GetStaffAsync` reload (add-user / disable-user already
   trigger a reload via `ChangeStaffAsync`). `SeatLimit` refreshes whenever account status is
   re-fetched. Ensure the staff reload path also re-raises `SeatUsageSummary` (step 3). If the task
   intends seat usage to count only *active* accounts, use `Staff.Count(m => m.IsActive)` instead of
   `Staff.Count` — **decision: use total accounts** (a disabled account still consumes a seat until
   deleted), matching "تعداد کاربر اضافه‌شده".

### Alternative (only if SeatLimit must live with the roster)
Add `int? SeatLimit` to `StaffDirectoryWire` + `StaffDirectory`, populate server-side in
`StaffProvisioningService.ListAsync` from the tenant grant. More files touched; **prefer the primary
path above**.

### Verify
`dotnet build src/Animora.Contracts`, `dotnet build src/Animora.Client.Services`,
`dotnet build src/Animora.Desktop`.

### Acceptance
Settings → users shows e.g. «۳ از ۵ کاربر مجاز»; number updates after adding/removing users and after
account-status refresh.

---

## TASK-1..6 — Integration / sync / standards / performance audit  ·  status: ☑ DONE

These are audit tasks; apply only *safe, surgical* fixes and record findings. Pre-resolved findings:

1. **Client↔server integration (TASK-1 & TASK-6 sync-integrity):** the live surface is
   login + `/auth/staff` + `/auth/device/*` + `/licensing/status`. All go through the shared
   `OnlineApi` (`src/Animora.Client.Services/Identity/OnlineApi.cs`) with `BearerAuthHandler`
   attaching the stored token. Error handling converts wire errors via `OnlineApi.ReadErrorCodeAsync`
   → `ServiceException`. **Finding:** consistent and standard. The TASK-7 correlation fix also
   improves provenance of synced rows' local re-emission. No change beyond TASK-7/8/9.

2. **Client-side UI bugs (TASK-2):** the device card previously could show a dead button; already
   fixed in prior round (approve/revoke feedback). The remaining concrete UI bug was the missing
   re-enable action for revoked devices → covered by TASK-9(a).

3. **Non-standard server items (TASK-3):** device re-enable was impossible server-side (no reinstate
   path) and self-revoke was unguarded → both are standardized by TASK-9(a)/(b). Seat data was present
   on the wire but unused → TASK-8.

4. **Future server risks (TASK-4):** seat-limit **enforcement** on staff creation is still a
   placeholder (S16) — `StaffProvisioningService` does not reject creation past `SeatLimit`.
   **Record as a follow-up** (out of scope for a client-visible fix now; TASK-8 only *displays*
   usage). Do NOT implement enforcement here unless explicitly asked — it needs the grant plumbed
   into the staff-create path and careful error semantics.

5. **Performance (TASK-5):** `ActivityDetailResolver.ResolveActorNameAsync` does a per-row user
   lookup but supports a `cache` dictionary; the history query path should pass a shared cache when
   resolving many rows. **Verify** the batch history query passes a cache; if it already does, no
   change. The TASK-7 fix adds no per-row cost (actor id now comes from ambient correlation, name
   resolution already existed).

6. **Dead code / cleanliness:** `src/Animora.Client.Services/Diagnostics/UiCorrelationScope.cs` is
   **never referenced** (grep shows only its own definition). It was presumably intended to seed UI
   correlation but was superseded by the dispatcher pipeline. **Safe fix:** delete the file
   (reduces confusion; nothing imports it). Confirm with a final `grep -rln "UiCorrelationScope" src/`
   returning only the file itself before deleting.

### Audit result (recorded)
- TASK-1/6: live surface verified consistent (shared `OnlineApi` + `BearerAuthHandler`); no change.
- TASK-2/3: re-enable + self-revoke gaps closed by TASK-9; seat data surfaced by TASK-8.
- TASK-4: seat-limit enforcement on staff creation (S16) explicitly **deferred** as a follow-up.
- TASK-5: verified — actor names are resolved once at write time (`ActivityFeedWriter` / `SyncedActivityFeed`)
  and stored on the row, so history reads do no per-row user lookup; no change needed.
- Dead code: `UiCorrelationScope.cs` deleted (only its own definition + historical roadmap docs referenced it).

### Acceptance
Findings recorded (this section). Safe fixes applied: delete dead `UiCorrelationScope.cs`. Follow-up
(seat enforcement S16) explicitly deferred.

---

## Execution order & push checkpoints (LOCKED)

1. **TASK-7** (CorrelationBehavior actor seeding) → `scripts/git/push.sh "fix(history): stamp real actor on locally-authored activity (TASK-7)"`
2. **TASK-9** (device reinstate + self-revoke guard + verify OS) → push
   `"feat(devices): re-enable revoked device, block manager self-revoke (TASK-9)"`
   (if the diff is large, push domain+server first, then client+UI separately)
3. **TASK-8** (seat usage display) → push `"feat(profile): show staff seat usage vs clinic limit (TASK-8)"`
4. **TASK-1..6** (delete dead UiCorrelationScope + audit notes) → push
   `"chore(cleanup): remove dead UiCorrelationScope; integration audit notes (TASK-1..6)"`
5. **FINAL** — delete this plan file (`Plan/client-server-integration-audit.md`) as the last plan
   step, then push `"chore(plan): remove completed integration-audit plan"`.
   > The uploaded task file `/home/user/uploaded_files/pasted-text-*.txt` is outside the repo and is
   > removed separately (not a repo file).

## Status legend
☐ TODO · ◐ IN-PROGRESS · ☑ DONE — update each task's `status:` line as you go, and push after each.
