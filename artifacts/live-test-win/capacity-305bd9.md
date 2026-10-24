# Animora live capacity report

> **Planning reference only.** Numbers come from one run against the live server from one client machine;
> network distance, client CPU and background load on the server all affect them. Do not treat them as a guarantee.

- **Run id:** `305bd9`  ·  **Server:** `http://188.212.99.234:5000`  ·  **Tenant:** `animora-loadtest`
- **Started:** 2026-10-24 10:31:15Z  ·  **Duration:** 1.5 min  ·  **Device sessions used:** 45
- **Health rule:** a level is unhealthy at >1% unexpected failures, p95 > 2000 ms, a failed `/health/ready` probe, or the service not active.
- **Server hardware monitor:** off (client-side metrics + /health/ready only; server-side metrics unavailable)

## Verdict

- **No level was healthy.** Even the lowest level broke: check connectivity/credentials first, then the hints below.
- **Load realism:** every virtual user pauses 2000–9000 ms between actions (log-uniform) and keeps a background sync poll every ~5s — the cadence of the production desktop — so one virtual user ≈ one real clinic desktop. The ceiling is therefore a DESKTOP count, not a synthetic request rate.
- **sales-rush:** healthy up to 0, broke at 45 — limiter: **unknown**. Latency/failures broke but server hardware was not observed. Re-run with the SSH monitor (ANIMORA_LT_SSH=1) to learn which resource to upgrade.

## Profile `sales-rush`

Write-heavy money path: each request = create invoice → add line → post → register payment (4 commands).

| Users | Requests | Throughput/s | p50 ms | p95 ms | p99 ms | Failures | Busy (503/429) | Host CPU avg/peak | App CPU avg/peak | App RSS | RAM avail min | PG conns peak | Health | Verdict |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 45 | 482 | 8.0 | 498 | 3042 | 3950 | 0 | 0 | n/a | n/a | n/a | n/a | n/a | 49/49 ready | BROKEN — p95 3042 ms > limit 2000 ms |

## Notes

- All created rows are prefixed `lt-cap<run>-` and exist only in the dedicated test tenant; the tool does not delete them (most aggregates have no delete verb). Reset the test tenant when convenient.
- 503 + Retry-After and 429 answers are the server's documented back-pressure: they are counted as *Busy*, never as failures.
- `<run>.json` next to this file carries the full per-second hardware time series for charts.
- **Host CPU** is the whole machine; **App CPU** is the `Animora.Server` process alone. Both are % of the machine, so they are directly comparable. If the load generator runs on the same host, Host CPU includes it — size a server by **App CPU** plus PostgreSQL.
