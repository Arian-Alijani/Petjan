# Plan — Owner/Pet delete + verify complete edit (Patients module, C01)

> **Self-contained execution plan.** A fresh AI model must be able to implement every task
> below **without re-investigating the repo**. All facts, file paths, line anchors, exact code
> patterns, and the key design decisions are pre-researched and written here. Read this file,
> then implement task-by-task. **After EACH task below: run `scripts/git/push.sh "msg"`** —
> pushing is mandatory and locked; skipping a push fails the project.

---

## 0. Repo orientation (read once, do not re-derive)

- **Project**: *Animora* — self-hosted veterinary clinic management desktop app. .NET 10,
  Avalonia + MVVM (CommunityToolkit.Mvvm), Persian/RTL UI, Jalali calendar, offline-first,
  local SQLite backend. All product code is under `src/`. Never touch `Animora-UI/`,
  `Database-Sampel/`, `windows-frontend-roadmap/`.
- **Build**: `scripts/sandbox/build.sh` (must be 0 errors / 0 warnings). ⚠️ The sandbox may
  have **no .NET SDK installed** (`[FAIL] No .NET SDK found. Run: scripts/sandbox/install-prereqs.sh`).
  Per the task rules **no full build / no long tests**. Only install + build if explicitly
  needed to verify a heavy change; otherwise rely on careful, pattern-matched edits.
- **Git push (LOCKED)**: `scripts/git/push.sh "type(scope): message"` — stages ALL, commits,
  rebases onto origin, pushes. Exit 2 = rebase conflict, 3 = no credentials (call
  `setup_github_environment` tool then re-run). Current branch: `main`.

### Architecture layers for this feature (Clean Architecture + CQRS)
Data flow for any write: **ViewModel** → `IPatientsQueryService` (Contracts) →
`PatientsService` (Client.Services, builds a command, resolves a scoped `IDispatcher` +
`ICurrentUserProvider` per call) → **Command + Handler** (Application/UseCases/Patients) →
**Repository** (Application/Ports + Persistence.Sqlite/Repositories) → `AnimoraLocalDbContext`.

---

## 1. THE key architectural decision — how "delete" works here (CRITICAL, pre-decided)

**Delete = soft-delete (tombstone), NOT physical removal.** This is the established pattern.

- Base class `SyncableEntity` (`src/Animora.Domain/Primitives/SyncableEntity.cs:45`) exposes
  `public bool Deleted { get; set; }`. Both `Owner` and `Pet` derive from `AggregateRoot : SyncableEntity`.
- A **global EF query filter** hides tombstoned rows automatically:
  `src/Animora.Persistence.Sqlite/Conventions/ModelConventions.cs:76` →
  `entity.HasQueryFilter("SoftDelete", e => !e.Deleted);` applied to **every** syncable root
  (via reflection in `ModelConventions.Apply`). So setting `entity.Deleted = true` instantly
  hides the record from **all** queries: directory list, detail, activity feed, AND the
  per-owner pet count `db.Set<Pet>().Count(p => p.OwnerId == o.Id)`
  (`PatientDirectoryQueries.cs:78` and `:175`) — no query-layer change needed.
- `AnimoraLocalDbContext.PrepareSave()` (`.../Context/AnimoraLocalDbContext.cs:~104`)
  auto-stamps `UpdatedAt = now` on any Added/Modified syncable entity on SaveChanges, so the
  tombstone propagates to sync for free. Setting `Deleted = true` on a tracked entity is enough.
- `AppendOnlyGuard` (`.../Context/AppendOnlyGuard.cs:19-21`) lists append-only types
  (JournalEntry, StockMovement, Payment, AuditEntry, …). **Pet and Owner are NOT in it**, so
  modifying them (incl. flipping `Deleted`) is allowed.

**Reference implementation to copy** (the canonical soft-delete command):
`DeleteProductCommand` in `src/Animora.Application/UseCases/Inventory/InventoryCommands.cs:314-370`.
Its handler ends with `product.Deleted = true; return Result<Unit>.Ok(Unit.Value);` after
history guards. Mirror this shape exactly.

### Cascade rule for owner delete (pre-decided)
Deleting an **owner** must also soft-delete **all of that owner's pets** in the same command
(so the user never has to delete pets first, and no orphan pets linger). We therefore load the
owner's pets via a new repo method and set `Deleted = true` on each + on the owner.
(Alternative "refuse if owner has pets" using the existing `OwnerHasActivePets` error was
rejected: worse UX. Cascade is cleaner and matches user intent "delete the owner".)

### Permission decision (pre-decided — avoids test churn)
**Reuse existing granular permissions. Do NOT add new permission codes.**
- Delete a pet → `[RequiresPermission(PermissionCodes.PatientsPetEdit)]` (same as `UpdatePetInfoCommand`).
- Delete an owner → `[RequiresPermission(PermissionCodes.PatientsOwnerEdit)]` (same as `UpdateOwnerCommand`).
- Reason: `tests/Animora.Domain.Tests/RolePermissionMatrixTests.cs` asserts *Manager holds the
  whole catalog* and `catalog.Count == PermissionCodes.All.Count`. Adding a new permission code
  would force editing `PermissionCodes.All`, `DefaultRolePermissions`, risk/critical sets, and
  the matrix test. Reusing the edit permission = zero permission-catalog changes, and matches
  the existing edit commands' authorization exactly.

### CommandTypes decision
Add **2** new wire command-type constants (used for auditing/journaling). Safe: tests only do
`Contains` checks on `CommandTypes.All` — there is **no strict count assertion** on it
(verified: no `CommandTypes.All.Count` / `Equal(...CommandTypes.All)` in tests). Must add the
new constants to the `CommandTypes.All` collection initializer too (declaration-order list).

---

## 2. Exact facts & file map (so no re-reading is needed)

### Contracts (the seam ViewModels depend on)
`src/Animora.Contracts/Patients/IPatientsQueryService.cs` — currently has:
`GetPetsPageAsync, GetOwnersPageAsync, GetDirectoryFacetsAsync, GetOwnerAsync,
GetOwnerActivityAsync, GetPetAsync, SaveOwnerAsync, SavePetInfoAsync, AddWeightEntryAsync,
CreatePetAsync, CreateOwnerAsync`. **No delete methods.** → add `DeletePetAsync`, `DeleteOwnerAsync`.

### Client service (builds commands, 1 scope per call)
`src/Animora.Client.Services/Patients/PatientsService.cs` (sealed, `IServiceScopeFactory scopes`).
Write-method template (copy this shape verbatim for the new deletes):
```csharp
public async Task DeletePetAsync(string petId)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(petId);
    await using var scope = scopes.CreateAsyncScope();
    var (dispatcher, provider) = Resolve(scope);
    var cc = await provider.BuildCommandContextAsync(null, null, null, default);
    if (cc.IsFailure) throw new ServiceException(cc.Error);
    var r = await dispatcher.SendAsync(new DeletePetCommand(petId), cc.Value!);
    if (r.IsFailure) throw new ServiceException(r.Error);
}
```
`Resolve(scope)` returns `(IDispatcher, ICurrentUserProvider)` (bottom of file). `ServiceException`
is the established failure channel. `DeleteOwnerAsync` is identical with `DeleteOwnerCommand`.
Return type: `Task` is fine (nothing to return). Match whatever the interface declares.

### Application commands (the real work) — pattern from `UpdatePetInfoCommand.cs` / `UpdateOwnerCommand.cs`
Both existing update commands are records implementing `ICommand<Unit>, IJournaledCommand` with
attributes `[RequiresPermission(...)]` + `[Auditable(AuditCategory.Clinical, "patients.pet.update")]`,
a validator, and a handler `ICommandHandler<TCommand, Unit>` that `FindAsync`s the entity, mutates,
returns `Result<Unit>.Ok(Unit.Value)` (TransactionBehavior commits). `AggregateTypeName` = "Pet"/"Owner",
`AggregateIdOrNull` = the id, `CommandTypeName` = the new wire constant.

Create a new file `src/Animora.Application/UseCases/Patients/DeletePatientsCommands.cs` with both
commands (or two files — either is fine). Shapes:

```csharp
// --- Pet delete (soft) ---
[RequiresPermission(PermissionCodes.PatientsPetEdit)]
[Auditable(AuditCategory.Clinical, "patients.pet.delete")]
public sealed record DeletePetCommand(string PetId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsPetDelete;
    public string AggregateTypeName => "Pet";
    public string? AggregateIdOrNull => PetId;
}
public sealed class DeletePetCommandValidator : IValidator<DeletePetCommand>
{   // mirror UpdatePetInfoCommandValidator: Rules.Required(c.PetId, nameof(c.PetId))
}
public sealed class DeletePetCommandHandler(IPetRepository pets) : ICommandHandler<DeletePetCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeletePetCommand c, CommandContext context, CancellationToken ct)
    {
        var pet = await pets.FindAsync(c.PetId, ct);
        if (pet is null) return Result<Unit>.Fail(ErrorCodes.PetNotFound);
        pet.Deleted = true;                 // global filter hides it; UpdatedAt auto-stamped
        return Result<Unit>.Ok(Unit.Value);
    }
}

// --- Owner delete (soft, cascades to pets) ---
[RequiresPermission(PermissionCodes.PatientsOwnerEdit)]
[Auditable(AuditCategory.Clinical, "patients.owner.delete")]
public sealed record DeleteOwnerCommand(string OwnerId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsOwnerDelete;
    public string AggregateTypeName => "Owner";
    public string? AggregateIdOrNull => OwnerId;
}
public sealed class DeleteOwnerCommandValidator : IValidator<DeleteOwnerCommand>
{   // Rules.Required(c.OwnerId, nameof(c.OwnerId))
}
public sealed class DeleteOwnerCommandHandler(IOwnerRepository owners, IPetRepository pets)
    : ICommandHandler<DeleteOwnerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteOwnerCommand c, CommandContext context, CancellationToken ct)
    {
        var owner = await owners.FindAsync(c.OwnerId, ct);
        if (owner is null) return Result<Unit>.Fail(ErrorCodes.OwnerNotFound);
        foreach (var pet in await pets.ListByOwnerAsync(c.OwnerId, ct))   // new repo method, see below
            pet.Deleted = true;
        owner.Deleted = true;
        return Result<Unit>.Ok(Unit.Value);
    }
}
```
**Usings** for the new command file (copy from `UpdateOwnerCommand.cs` header): `Animora.Application.Abstractions`,
`Animora.Application.Auditing`, `Animora.Application.Commands`, `Animora.Application.Ports`,
`Animora.Application.Security`, `Animora.Application.Validation`, `Animora.Contracts.Wire.Commands`,
`Animora.Domain.Identity`, `Animora.Domain.Patients`, `Animora.Domain.Results`. Namespace
`Animora.Application.UseCases.Patients`. `Rules.Required(...)` is in `Animora.Application.Validation`.

### Repository port change (for the cascade)
`src/Animora.Application/Ports/IPetRepository.cs` — add:
```csharp
/// <summary>Every live pet belonging to the owner (tracked, for cascade soft-delete).</summary>
Task<IReadOnlyList<Pet>> ListByOwnerAsync(string ownerId, CancellationToken ct = default);
```
Impl in `src/Animora.Persistence.Sqlite/Repositories/PetRepository.cs` (mirror `ListByOwnerAndNameAsync`):
```csharp
public async Task<IReadOnlyList<Pet>> ListByOwnerAsync(string ownerId, CancellationToken ct = default) =>
    await db.Set<Pet>().AsTracking().Where(e => e.OwnerId == ownerId).ToListAsync(ct);
```
(`AsTracking()` so the `Deleted = true` is persisted; global filter already excludes already-deleted.)

### Registration (DI) — REQUIRED or the commands won't resolve
`src/Animora.Application/UseCases/Patients/PatientsRegistration.cs` — add to the fluent chain:
```csharp
.AddCommand<DeletePetCommand, Unit, DeletePetCommandHandler>()
.AddValidator<DeletePetCommand, DeletePetCommandValidator>()
.AddCommand<DeleteOwnerCommand, Unit, DeleteOwnerCommandHandler>()
.AddValidator<DeleteOwnerCommand, DeleteOwnerCommandValidator>()
```
Also update the XML summary comment count ("5 journaled commands" → 7) for accuracy (optional).

### Wire command types
`src/Animora.Contracts.Wire/Commands/CommandTypes.cs` — in the Patients region (after
`PatientsPetWeightAdd`, ~line 77) add:
```csharp
public const string PatientsOwnerDelete = "patients.owner.delete";
public const string PatientsPetDelete   = "patients.pet.delete";
```
And add both to the `All` collection initializer (the Patients line near the bottom, ~line 115):
`PatientsOwnerCreate, PatientsOwnerUpdate, PatientsPetCreate, PatientsPetUpdate, PatientsPetWeightAdd, PatientsOwnerDelete, PatientsPetDelete,`.

### Error codes — already exist, no change needed
`ErrorCodes.PetNotFound` (`PET_NOT_FOUND`) and `ErrorCodes.OwnerNotFound` (`OWNER_NOT_FOUND`)
exist in `src/Animora.Domain/Results/ErrorCodes.cs:180,183`. (`OwnerHasActivePets` `:186` exists
but we use cascade, so it's NOT needed.)

### UI — confirmation dialog (reusable, pre-built)
`ConfirmDialogViewModel` at `src/Animora.Desktop/Pages/Sales/Shared/ConfirmDialogViewModel.cs`.
Constructor signature (positional):
`(IDialogService dialogs, string tone, string iconKey, string title, string bodyLead,
string bodyStrong, string bodyTrail, string bodyNote, string confirmLabel, string cancelLabel,
Action? onConfirm)`. `tone = "danger"` makes the confirm button red (`IsDanger`). On confirm it
calls `_dialogs.Close()` **then** `onConfirm()`. Invoke via `_dialogs.ShowModal(new ConfirmDialogViewModel(...))`
(pattern used in `InvoiceViewerViewModel.cs:216`). For `iconKey` reuse whatever the existing
danger confirms use (grep `new ConfirmDialogViewModel(` in `InvoiceViewerViewModel.cs` and copy a
danger one's icon/tone args — e.g. the invoice-delete prompt). Namespace to import in the VM:
`using Animora.Desktop.Pages.Sales.Shared;`.

### UI — where to add the delete buttons/commands
**Pet delete** → `src/Animora.Desktop/Pages/Patients/PetFileViewModel.cs`
(sealed `PageViewModelBase`; fields `_patients` (IPatientsQueryService), `_presenter`
(IRecordSheetPresenter), `_dialogs`, `_toast`, `_changes` (IDataChangeNotifier); `_pet` (PetDetail),
`_owner`). Existing `SaveAsync` at ~L577 ends with:
`_changes.Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);`
then `_toast.Show("مشخصات بیمار به‌روزرسانی شد", Controls.Tone.Ok);`. Add a `[RelayCommand] private void Delete()`
that shows the confirm dialog; on confirm (make the lambda `async`):
```csharp
await _patients.DeletePetAsync(_pet!.Id);
_changes.Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
_dialogs.Close();                       // close the pet sheet
_toast.Show("پرونده بیمار حذف شد", Controls.Tone.Ok);
```
Add a red "حذف بیمار" button to `PetFileView.axaml` bound to `DeleteCommand` (place it with the
Edit/Save controls; copy styling of an existing danger button, e.g. Inventory ProductModalView's
"حذف کالا" button at `ProductModalView.axaml:741-745`).

**Owner delete + pet-row delete** → `src/Animora.Desktop/Pages/Patients/OwnerSheetViewModel.cs`
(sealed `PageViewModelBase`; fields `_patients`, `_presenter`, `_dialogs`, `_toast`, `_changes`,
`_sales`, `_vaccination`; `_owner` (OwnerDetail); `Pets` is `ObservableCollection<OwnerSheetPetRowViewModel>`,
each row `.Pet.Id`). Existing patterns to mirror: `SaveAsync` (L381) uses
`_patients.SaveOwnerAsync(...)` → `_changes.Notify(...)` → `Apply(saved)`; `AddPet` (L413) uses
`_dialogs.ShowModal(...)` then `await LoadAsync(owner.Id)` on success; `OpenPet` (L~458) =
`_presenter.OpenPetSheetAsync(row.Pet.Id)`.
Add:
- `[RelayCommand] private void Delete()` (delete whole owner): confirm → on confirm `async`:
  ```csharp
  await _patients.DeleteOwnerAsync(_owner!.Id);
  _changes.Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
  _dialogs.Close();                     // close the owner sheet
  _toast.Show("پرونده مالک و حیوانات مرتبط حذف شد", Tone.Ok);
  ```
  Confirm body should warn that the owner's pets are also removed.
- `[RelayCommand] private void DeletePet(OwnerSheetPetRowViewModel? row)` (delete one pet from the
  owner's roster): guard `row is null` → confirm → on confirm `async`:
  ```csharp
  await _patients.DeletePetAsync(row.Pet.Id);
  _changes.Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
  await LoadAsync(_owner!.Id);          // refresh roster so the pet disappears
  _toast.Show("حیوان از پرونده مالک حذف شد", Tone.Ok);
  ```
  `Tone` here is `Animora.Desktop.Controls.Tone` (OwnerSheetVM already uses bare `Tone.Ok`).
Add a red "حذف مالک" button to `OwnerSheetView.axaml` (near Edit/Save) and a small delete (trash)
button per pet row bound to `DeletePetCommand` with `CommandParameter="{Binding}"` (copy the
per-row delete button pattern from `ProductModalView.axaml:499` which uses
`CommandParameter="{Binding}" ToolTip.Tip="حذف"`).

> **Find the exact XAML files**: `OwnerSheetView.axaml` and `PetFileView.axaml` live in
> `src/Animora.Desktop/Pages/Patients/`. Open each only to locate the Edit/Save button group and
> the pet-row `ItemsControl`/`DataTemplate`; insert the new buttons there. Keep RTL + existing
> button styles.

### Tests (do NOT break; optionally extend lightly)
`tests/Animora.Application.Tests/PatientsUseCaseTests.cs` is the place to add a focused delete
test if time permits (not required by the task; the task forbids long/full test runs). A minimal
addition would mirror an existing update test: build the test harness, send `DeletePetCommand`,
assert `Result` success and that a subsequent `GetPetAsync`/directory query no longer returns it.
**Only add if quick and safe; skip if it risks a long build.** No existing test asserts a strict
count on `CommandTypes.All`, and permissions are reused, so the 2 new command types + 2 new
commands do not break `RolePermissionMatrixTests` or wire tests.

---

## 3. Task list (implement in order; **push after each**)

> Status legend: `[ ]` todo · `[x]` done. Update as you go.

- [x] **T1 — Repo port: add `ListByOwnerAsync`.**
  Edit `IPetRepository.cs` (add method) + `PetRepository.cs` (impl, see §2). Push:
  `scripts/git/push.sh "feat(patients): add IPetRepository.ListByOwnerAsync for owner cascade"`

- [x] **T2 — Wire command types.**
  Add `PatientsOwnerDelete`, `PatientsPetDelete` constants + add both to `CommandTypes.All`
  in `CommandTypes.cs`. Push:
  `scripts/git/push.sh "feat(patients): register owner/pet delete command types"`

- [x] **T3 — Application commands + handlers + validators.**
  Create `DeletePatientsCommands.cs` with `DeletePetCommand`/`DeleteOwnerCommand` (+ validators +
  handlers) per §2. Register all four in `PatientsRegistration.cs`. Push:
  `scripts/git/push.sh "feat(patients): soft-delete owner (cascade pets) and pet commands"`

- [x] **T4 — Contracts + client service.**
  Add `DeletePetAsync(string)` and `DeleteOwnerAsync(string)` to `IPatientsQueryService.cs`;
  implement both in `PatientsService.cs` (copy the write-method shape in §2). Push:
  `scripts/git/push.sh "feat(patients): expose DeletePetAsync/DeleteOwnerAsync on patients service"`

- [ ] **T5 — Pet File UI delete.**
  Add `DeleteCommand` to `PetFileViewModel.cs` (confirm dialog → `DeletePetAsync` → notify →
  close sheet → toast) and a red "حذف بیمار" button to `PetFileView.axaml`. Push:
  `scripts/git/push.sh "feat(patients): delete pet from Pet File with confirm"`

- [ ] **T6 — Owner Sheet UI delete (owner + per-pet).**
  Add `DeleteCommand` (owner, cascade) and `DeletePetCommand(row)` to `OwnerSheetViewModel.cs`
  (confirm dialogs → service calls → notify → close/reload → toast); add a red "حذف مالک" button
  and a per-row trash button to `OwnerSheetView.axaml`. Push:
  `scripts/git/push.sh "feat(patients): delete owner (cascade) and remove a pet from Owner Sheet"`

- [ ] **T7 — Verify edit completeness (Task 2 of the brief).**
  Confirm owner & pet edit already cover all intended fields and work end-to-end — **no guessing,
  read the existing edit paths once**:
  - Owner edit: `OwnerSheetViewModel.SaveAsync` (L381) builds `OwnerDetail with { FirstName,
    LastName, NationalId, Gender, Mobile1, Mobile2, Phone, Address, Referral }` →
    `SaveOwnerAsync` → `UpdateOwnerCommand`. **Known gap to check**: `SaveOwnerAsync` →
    `UpdateOwnerCommand` only carries `FullName, PhonePrimary, Gender, PhoneSecondary, NationalId,
    Address`. Fields edited in the UI but **not persisted by the command**: `Phone` (landline),
    `Referral`, `Email`, `City`, `Notes`. `UpdateOwnerCommandHandler` calls
    `owner.UpdateContactDetails(..., email: owner.Email, city: owner.City, notes: owner.Notes)` —
    i.e. it *preserves* existing values but cannot *change* Email/City/Notes, and does not handle
    Phone/Referral at all. **Decision for T7**: if the UI exposes editing those fields, extend
    `UpdateOwnerCommand` (+ its mapping in `PatientsService.SaveOwnerAsync`, + `OwnerDetail`
    carries them) so every editable field actually saves. If the UI does NOT expose them, leave
    as-is (no dead params). Verify by reading `OwnerSheetView.axaml` edit inputs vs. the command
    params. Keep `Owner.UpdateContactDetails` signature in mind (`Owner.cs:103`).
  - Pet edit: `PetFileViewModel.SaveAsync` (L577) → `SavePetInfoAsync` → `UpdatePetInfoCommand`
    (Name, Breed, Color, Gender, BirthUtc, Neutered, AllergyNotes, ChronicNotes). **Species is
    intentionally immutable** (`PET_SPECIES_IMMUTABLE`, by design — do NOT make it editable).
    Microchip has its own path (`Pet.SetMicrochip`, `UpdatePetInfoCommand` does not touch it) —
    verify whether the UI edits microchip and, if so, that it persists; otherwise leave it.
  - Make the **minimal** change needed so "all editable fields truly save". Push any change:
    `scripts/git/push.sh "fix(patients): persist all editable owner/pet fields on save"`
    (If no change is needed, note it in this file and skip the push for T7.)

- [ ] **T8 — (Optional, only if fast & SDK present) focused delete test** in
  `PatientsUseCaseTests.cs`. Skip if it risks a long build. If added, push:
  `scripts/git/push.sh "test(patients): cover owner/pet soft-delete"`

- [ ] **T9 — FINAL: delete this plan file.**
  `rm Plan/owner-pet-delete-edit.md` then
  `scripts/git/push.sh "chore(plan): remove completed owner/pet delete+edit plan"`
  (Do NOT touch any other file in `Plan/` or any roadmap file.)

---

## 4. Definition of done
1. A pet can be deleted from the Pet File and from the Owner Sheet pet roster (confirm → soft-delete
   → row/sheet disappears, directory counts update) — all via commands, authorized, audited.
2. An owner can be deleted from the Owner Sheet; the owner **and all their pets** are soft-deleted
   (cascade); sheet closes; directory no longer lists them.
3. Owner & pet edit persist **every** field the UI exposes (species stays immutable by design).
4. New commands are DI-registered; wire command types registered; permissions reused (no catalog
   churn); no test broken.
5. Every task above was pushed via `scripts/git/push.sh` (locked rule).
6. This plan file is removed as the final commit (T9).
