# Animora.Client.Services

Real implementations of the `Animora.Contracts` interfaces, built over
`Animora.Application` use cases and the local `Animora.Persistence.Sqlite` store
(plus `Animora.Infrastructure.Local` adapters). This is the DI **swap target**
that replaces the in-memory `Mock*` services in `Animora.Desktop` — one bounded
context at a time throughout **Track C** (per the mock-retirement protocol,
CONVENTIONS.md §10).

Currently an empty scaffold (Phase A01 only creates the project and its
references). `Animora.Desktop` does not reference this project yet — that
reference is added by **C01**, the first phase that actually swaps a service.
