# Phase A11a — Keystore, key service, installation identity, options, error codes (session 1/4 of A11)

**Track:** A · **Depends on:** A10 ✅ (A10d) · **Next:** A11b · Satisfies (with A11b–d): R-B02, R-B09, R-B27, R-B28
Split of original A11 (A11a→A11d, one session each).

## Session rules (keep token use low)
- Read ONLY this file + "Read only" files. No other phase files, UI, mocks, tests.
- Rules: `CONVENTIONS.md` §7 (key in OS keystore, fallback opt-in/logged, missing key fails closed) only.
- **No tests / no `test.sh`** (owed to A12). No migration here (A11b). **Zero network**: no Http/Socket/WebClient anywhere in `Infrastructure.Local`.
- No edits to Desktop/`App.axaml.cs`/mocks/Contracts. dotnet not on PATH → `scripts/sandbox/AI_GUIDE.md`.

## Pre-made architecture decisions (apply for all of A11a–d)
- `Infrastructure.Local` gets a `ProjectReference` to `Animora.Persistence.Sqlite` (no cycle: Persistence never references Infrastructure.Local; Client.Services references both; layering test only restricts Domain/Application/Contracts).
- All Infrastructure.Local registrations live in `src/Animora.Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs` → `AddAnimoraLocalInfrastructure(this IServiceCollection, Action<LocalStoreOptions>? configure = null)`, which calls `AddAnimoraLocalStore(configure)` then adds its own services. Persistence-only types stay registered in `AddAnimoraLocalStore`. Each A11 session appends its registrations here.
- Existing seam `Persistence.Sqlite/Context/IStoreKeyProvider.cs` (`string? GetKey()`, `NullStoreKeyProvider`) is kept; the real provider is `LocalDbKeyService` (Infrastructure.Local) and replaces the Null registration inside `AddAnimoraLocalInfrastructure`.

## Read only
`src/Animora.Infrastructure.Local/*.csproj` + `README.md`, `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs`, `Context/IStoreKeyProvider.cs`, `src/Animora.Domain/Results/ErrorCodes.cs` (tail only), `Directory.Packages.props`.

## Tasks
1. **ErrorCodes** (append, category `ErrorCode.Infrastructure` unless noted): `StoreKeyUnavailable "STORE_KEY_UNAVAILABLE"`, `NativeSqliteTooOld "NATIVE_SQLITE_TOO_OLD"`, `StoreCorrupt "STORE_CORRUPT"`, `StoreBackupFailed "STORE_BACKUP_FAILED"`, `StoreRestoreFailed "STORE_RESTORE_FAILED"`, `StoreEncryptionNotApproved "STORE_ENCRYPTION_NOT_APPROVED"`, each with a one-line `<summary>`.
2. **LocalStoreOptions** += `bool AllowInsecureKeyFallback` (false), `bool K2Approved` (false), `int WalCheckpointPages` (1000), `string BackupDirectory` (default `<dir of DatabasePath>/backups`, computed when empty), `int BackupRetentionCount` (5). Add `Result Validate()`: `EncryptionEnabled && !K2Approved` → `StoreEncryptionNotApproved`; non-positive retention/checkpoint → validation failure. Also expose `DataDirectory` (dir of `DatabasePath`).
3. **Security/ISecretStore.cs** — `Result<byte[]> TryRead(string name)`, `Result Write(string name, byte[] value)`, `Result Delete(string name)`; names are ASCII keys (validate `^[a-z0-9-]+$`), e.g. `"local-db-key"`.
4. **Security/DpapiSecretStore.cs** (`[SupportedOSPlatform("windows")]` to keep 0 warnings) — `ProtectedData.Protect/Unprotect`, `DataProtectionScope.CurrentUser`, blob at `%LOCALAPPDATA%/Animora/secrets/<name>.bin`, directory/file ACL restricted to current user. Needs package `System.Security.Cryptography.ProtectedData`: add ONE exact central pin in `Directory.Packages.props` on the 10.0.x line already used (10.0.12) + reference in the csproj.
5. **Security/KeychainSecretStore.cs** (`[SupportedOSPlatform("macos")]`) — `Process` running `/usr/bin/security add-generic-password -U -s Animora -a <name> -w <b64>` / `find-generic-password -s Animora -a <name> -w` / `delete-generic-password`; no P/Invoke, no third-party package; nonzero exit → failed Result (never throw to caller); don't log secret values.
6. **Security/FileFallbackSecretStore.cs** — dev only, plaintext `<data>/secrets/<name>.key`; constructor refuses unless `AllowInsecureKeyFallback`; `ILogger` warning on **every** read.
7. **Security/LocalDbKeyService.cs** — `sealed class LocalDbKeyService : IStoreKeyProvider`: `Result<string> GetOrCreatePassphrase()` (first run: 32 bytes `RandomNumberGenerator` → base64 → `Write`; later: read). Read failure with `EncryptionEnabled` and no fallback → `StoreKeyUnavailable` (fail closed — caller must refuse to open the store). `KeySource Source` property (`None` when encryption off, `OsKeystore`, `InsecureFallback`). `GetKey()` returns null when encryption off, else passphrase or throws `InvalidOperationException` carrying `STORE_KEY_UNAVAILABLE` (the interceptor must never open unkeyed). Define `public enum KeySource { None, OsKeystore, InsecureFallback }` in `Persistence.Sqlite/Maintenance/KeySource.cs` (used by diagnostics in A11c).
8. **Security/InstallationIdentity.cs** — `string ComputeMachineFingerprintHash()` = lowercase hex SHA-256 of `machine name + OS install marker + primary volume serial` (Windows: `MachineGuid` registry value + system drive serial via `DriveInfo`/`GetVolumeInformation`-free fallback; macOS: `IOPlatformUUID` via `ioreg` local process; Linux/unknown: `/etc/machine-id`); missing parts → stable empty string, never throw. Anti-cloning anchor for D11/E07.
9. **DI**: create `AddAnimoraLocalInfrastructure` — `ISecretStore` chosen once by `OperatingSystem.IsWindows()/IsMacOS()`, else (or when fallback allowed and OS store unavailable) `FileFallbackSecretStore`; `LocalDbKeyService` singleton registered also as `IStoreKeyProvider` (replace Null); `InstallationIdentity` singleton. Update `Infrastructure.Local/README.md` owners line (A11a–d).

## Gate
- `scripts/sandbox/build.sh full` = 0/0 (no CA1416 platform warnings).
- `grep -rn "Http\|Socket\|WebClient" src/Animora.Infrastructure.Local` → nothing.

## Handoff
`backend-roadmap/PROGRESS.md`: row A11a ✅ + date + note (secret stores, pin added, key-service fail-closed, KeySource location, DI entry point name); Next → `A11b`. Commit `feat(local): OS keystore, db key service and installation identity (A11a)`. Final answer: done / deviations / blockers; never claim unrun builds.
