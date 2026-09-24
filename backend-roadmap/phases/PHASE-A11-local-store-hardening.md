# Phase A11 — Local store hardening (index only — split into 4 sessions)

**Satisfies:** R-B02, R-B09, R-B27, R-B28 · **Depends on:** A10 (A10d). Do not implement from this file. Execute one sub-phase per session, in order; each file is self-contained:

| Session | File | Scope |
|---|---|---|
| A11a | `PHASE-A11a-keystore-options-errors.md` | 6 error codes, options + `Validate()`, secret stores (DPAPI/Keychain/fallback), `LocalDbKeyService`, `InstallationIdentity`, `AddAnimoraLocalInfrastructure` |
| A11b | `PHASE-A11b-encryption-seam-local-entities-migration-003.md` | encryption provider seam (K2-gated), interceptor rewrite, 3 local-only entities, tenant from `LocalInstallation`, migration `003_LocalHardening` |
| A11c | `PHASE-A11c-native-assertion-maintenance-filestore.md` | native SQLite ≥ 3.50.2 assertion, diagnostics, backup/restore/quarantine/vacuum, `LocalFileStore` |
| A11d | `PHASE-A11d-clocks-startup-di-closeout.md` | `SystemClock`, `MonotonicClock` high-water, 8-step `LocalStoreStartup`, final DI, A11 close-out + A12 debt |

A11 is ✅ only when A11d is done. Track A policy unchanged: no tests (owed to A12).
