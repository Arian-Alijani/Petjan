# D35 — scoped authenticated dashboard pipeline evidence

Captured 2026-10-01 on the Linux sandbox (Debian 13, 2 CPUs, .NET 10.0.12, **Debug** build, ~1 GiB RAM).
Harness: `tests/Animora.Application.Tests/DashboardEvidenceMeasurements.cs` — opt-in
(`ANIMORA_DASHBOARD_EVIDENCE_OUTPUT=<file>`), no assertions on time, never runs in normal test passes.

```bash
export ANIMORA_DASHBOARD_EVIDENCE_OUTPUT="$PWD/.sandbox/logs/d35-evidence.json"
dotnet test tests/Animora.Application.Tests/Animora.Application.Tests.csproj \
  --filter FullyQualifiedName~DashboardEvidenceMeasurements
```

## What is measured (scope — read before quoting any number)

`DashboardQueryService` as the Desktop calls it, **after a real local sign-in** (first-run setup, live session,
membership, role definition): authorization → source-generation probe → dispatcher → queries, on a temporary
migrated SQLite file (never the user's database). Data volume: 200 patients, 1 owner, 1 daily finance fact.
SQL statements are counted by a command interceptor on every connection the call opens.

| Metric | Value | Scope |
|---|---:|---|
| Cold first authenticated read (JIT + EF warm-up + empty cache) | 476.9 ms / 24 SQL | one sample, this process |
| Warm cache hit (re-authorizes + generation probe) | 11.8 ms median of 5 / 6 SQL | cache hit, D03/D22 |
| Warm forced miss (full recompose) | 21.9 ms median of 5 / 24 SQL | miss, D24 budget |
| Read after ONE controlled source write (cache invalidated, recomposed) | 17.2 ms | consistency latency, D19/D21 |

The SQL counts equal the D24 budget (hit 6, miss ≤ 26 per role), so the harness also confirms the pipeline did not regress.

## What is NOT measured (remains open, deliberately not faked)

- **Login → dashboard first render** (Avalonia layout + compiled-binding materialization + chart geometry): needs an
  interactive authenticated GUI session. The sandbox only has Xvfb and the first-run account creation is interactive;
  adding production instrumentation or changing the local scripts is outside D35. No marker, no number.
- Working set of an authenticated, navigated shell (the C11 value is the login window after 60 s idle).
- Windows/real-DPI numbers, and any hit/miss timing on the user's real database.
- The historical C11 figures (`C11-offline-budget.md`: dashboard 3.442 ms *query proxy*, first frame 1564/6215 ms
  *login proxy*) are a different scope (no authorization, no generation probe) and must not be compared with this table.

No bottleneck was found by this run; any future one needs its own evidence-backed task. Debug numbers on a shared
2-CPU host describe this run only, not production capacity.
