# Animora.Infrastructure.Local

Local (on-device) infrastructure adapters that implement Application ports over
OS facilities: clocks/`TimeProvider`, OS keystore + encryption provider, local
files, crash handling, and logging sinks.

Filled by phases:
- **A02** — clocks (`IClock` / `TimeProvider`).
- **A11a–d** — OS keystore (`Security/`: DPAPI / Keychain / dev file fallback), `LocalDbKeyService`
  (fail-closed store key), `InstallationIdentity`, then migration/maintenance/diagnostics (A11b–d).
  DI entry point: `AddAnimoraLocalInfrastructure` (wraps `AddAnimoraLocalStore`).
- **A11d (now present):** `Time/SystemClock` (`IClock`), `Time/MonotonicClock` (`IMonotonicClock`, persisted
  never-decreasing high-water), `Startup/LocalStoreStartup` (`ILocalStoreInitializer`: 8 steps — private dir,
  options+key, open+native assert, quick_check/quarantine, migrate, WAL checkpoint, bootstrap+seed, diagnostics).
  Also `Storage/LocalFileStore`, `Startup/NativeSqliteAssertion`. Desktop wiring is C01 (still on mocks).
- **B05** — local logging.
