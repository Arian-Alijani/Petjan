# Phase A10 — Local persistence II (index only — split into 4 sessions)

Do not implement from this file. Execute one sub-phase per session, in order; each file is self-contained:

| Session | File | Scope |
|---|---|---|
| A10a | `PHASE-A10a-owned-rows-business-mappings.md` | owned rows as relational tables, exact Money/Quantity columns, fixed-scale quantity, indexes |
| A10b | `PHASE-A10b-guard-uow-dispatcher-di.md` | append-only guard, repositories, atomic UoW, typed dispatcher, DI + DbContextFactory |
| A10c | `PHASE-A10c-reference-seeding.md` | SeedMarker + idempotent LocalSeeder (tenant, permissions/roles, accounts, units, categories, rooms, period) |
| A10d | `PHASE-A10d-read-queries-migration-002.md` | paging + stock/invoice/ledger aggregate queries, migration `002_LocalBusinessSchema`, A10 close-out + A12 debt |

A10 is ✅ only when A10d is done. Track A policy unchanged: no tests (owed to A12).
