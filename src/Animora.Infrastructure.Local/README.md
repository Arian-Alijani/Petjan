# Animora.Infrastructure.Local

Local (on-device) infrastructure adapters that implement Application ports over
OS facilities: clocks/`TimeProvider`, OS keystore + encryption provider, local
files, crash handling, and logging sinks.

Currently an empty scaffold (Phase A01 only creates the project and its
references). It is filled by later phases:
- **A02** — clocks (`IClock` / `TimeProvider`).
- **A11** — OS keystore + encryption provider.
- **B05** — local logging.
