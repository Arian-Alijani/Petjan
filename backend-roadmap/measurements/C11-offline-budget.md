# C11 offline budget measurements

Captured 2026-09-30T05:31:20.067092+00:00 on Linux sandbox, Debug .NET 10,
Xvfb virtual display, no server/HTTP transport. CPU: Intel(R) Xeon(R) Processor @ 2.50GHz. Host MemTotal:        1008784 kB; cgroup memory.max=unknown.
GC heap limit 576 MiB, workstation GC. Results describe this run only, not production capacity.

| Metric | Measured | Budget | Result | Method |
|---|---:|---:|---|---|
| First visible frame (login, Main proxy) | 1564.504 ms | ≤ 1200 ms | OVER BUDGET | Main → LoginWindow.Opened stopwatch |
| Dashboard data (query proxy, not render) | 3.442 ms | ≤ 2500 ms | PASS | Warm DashboardComposer median of 5, separate migrated DB |
| Working set after 60 s idle | 285.656 MiB | ≤ 300 MiB | PASS | Last /proc VmRSS sample; login idle, not authenticated shell |
| Patients page (200 rows) | 2.163 ms | ≤ 120 ms | PASS | Real PatientDirectoryQueries, assert exactly 200, warm median of 5 |
| Global search keystroke | 0.624 ms | ≤ 80 ms | PASS | Real five-source global search, warm median of 5 |
| Post invoice (10 lines) | 26.857 ms | ≤ 250 ms | PASS | Real dispatcher + journal + UoW + ledger, four independent warm posts |

## Measurement scope and acceptance limitations

The product intentionally opens its login gate before MainWindow. The first-frame marker is the real
LoginWindow.Opened event, not RSS polling; it is a documented proxy for MainWindow.Opened.
The dashboard value measures warm data composition on a separate migrated file DB, **not** the
first-frame-to-render interval. Idle memory is the real login process after 60 seconds, **not** an
authenticated, fully navigated shell. These proxies must not be represented as proof of the full UI
budgets. A manual authenticated 17-screen pass and the two render/shell measurements remain reviewer work.

Patients/search measurements use 200 real patient rows. Ten-line invoice posts execute the complete
production command pipeline (including durable journal, audit, outbox and balanced ledger), with a warm-up
post excluded. Budgets are not xUnit assertions; bench exits non-zero only for >2× measured regressions,
missing measurements or runtime failures. Full build/full test suite were not run by user directive.

## Execution evidence and cold-cache observation

- SDK: 10.0.401. Final run: `bench.sh offline`, exit **0**; process alive at end,
  `wall_ms=60053`, `peak_rss_kb=292512`. This is a repeat run on warmed filesystem/font caches,
  not a cold-start certification. First frame remains **OVER BUDGET**, below the 2400 ms failure ceiling.
- Entire run (VSTest operations and GUI) used `unshare -Urnm`: only namespace-local `lo`
  (`127.0.0.1/8`, `::1/128`), no external interface and an empty route table. Loopback was
  explicitly enabled for VSTest/testhost IPC, not for a product transport. Xvfb used `-nolisten tcp`.
- Initial fresh-cache diagnostic run measured startup **6215.490 ms**, triggering the correct
  **non-zero** >2× budget exit; its idle sample was **327.293 MiB**. It also exposed that the old
  whole-second deadline stopped at `wall_ms=59683`, so that memory sample is **not** valid as a full
  60-second idle measurement. The benchmark now uses elapsed milliseconds and the final sample
  above waits the full interval. The cold startup observation is retained, not erased by the rerun.
- Initial warm queries: dashboard 4.009 ms, patients 2.274 ms, search 0.662 ms, post 15.488 ms.
  These were real measurements, but the run as a whole failed; the table above is the complete
  successful invocation. No claim is made that cold startup or authenticated rendering meets budget.
- Search includes `GlobalSearchQueryHandler` normalization of the mixed-case keystroke and asserts
  exactly eight `pet` results; an empty-query fast path cannot satisfy this harness.
- Sandbox X11 libraries, SDK, HOME/XDG directories and temp DBs were installed/stored under
  `.sandbox/` only. X sockets used a private mount of `.sandbox/tmp` over `/tmp`; `xkbcomp`
  was supplied through a namespace-local `/usr/bin` overlay, without changing the system.
