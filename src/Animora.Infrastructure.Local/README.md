# Animora.Infrastructure.Local

Local (on-device) infrastructure adapters that implement Application ports over
OS facilities: clocks/`TimeProvider`, OS keystore + encryption provider, local
files, crash handling, and logging sinks.

Filled by phases:
- **A02** — clocks (`IClock` / `TimeProvider`).
- **A11a–d** — OS keystore (`Security/`: DPAPI / Keychain / dev file fallback), `LocalDbKeyService`
  (fail-closed store key), `InstallationIdentity`, then migration/maintenance/diagnostics (A11b–d).
  DI entry point: `AddAnimoraLocalInfrastructure` (wraps `AddAnimoraLocalStore`).
- **B05** — local logging.
