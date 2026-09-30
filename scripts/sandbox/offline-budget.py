#!/usr/bin/env python3
"""Record hardware-dependent offline measurements; only >2x budget is a failing exit."""
import datetime
import json
import math
import pathlib
import re
import sys

app_log, query_json, rss_kb, output = sys.argv[1:]
markers = re.findall(r"ANIMORA_BENCH first_frame_ms=([0-9.]+)", pathlib.Path(app_log).read_text())
if not markers:
    raise SystemExit("Missing actual Window.Opened marker; RSS threshold is not a frame measurement")
queries = json.loads(pathlib.Path(query_json).read_text())
metrics = [
    ("First visible frame (login, Main proxy)", float(markers[0]), 1200, "ms", "Main → LoginWindow.Opened stopwatch"),
    ("Dashboard data (query proxy, not render)", queries["dashboard_ms"], 2500, "ms", "Warm DashboardComposer median of 5, separate migrated DB"),
    ("Working set after 60 s idle", int(rss_kb) / 1024, 300, "MiB", "Last /proc VmRSS sample; login idle, not authenticated shell"),
    ("Patients page (200 rows)", queries["patients_ms"], 120, "ms", "Real PatientDirectoryQueries, assert exactly 200, warm median of 5"),
    ("Global search keystroke", queries["search_ms"], 80, "ms", "Real five-source global search, warm median of 5"),
    ("Post invoice (10 lines)", queries["post_ms"], 250, "ms", "Real dispatcher + journal + UoW + ledger, four independent warm posts"),
]
if any(not math.isfinite(value) or value <= 0 for _, value, *_ in metrics):
    raise SystemExit("Invalid measurement")
cpu = next((line.split(':', 1)[1].strip() for line in pathlib.Path('/proc/cpuinfo').read_text().splitlines()
            if line.startswith('model name')), 'unknown')
mem = next(line for line in pathlib.Path('/proc/meminfo').read_text().splitlines() if line.startswith('MemTotal:'))
limit_file = pathlib.Path('/sys/fs/cgroup/memory.max')
limit = limit_file.read_text().strip() if limit_file.exists() else 'unknown'
text = f"""# C11 offline budget measurements

Captured {datetime.datetime.now(datetime.timezone.utc).isoformat()} on Linux sandbox, Debug .NET 10,
Xvfb virtual display, no server/HTTP transport. CPU: {cpu}. Host {mem}; cgroup memory.max={limit}.
GC heap limit 576 MiB, workstation GC. Results describe this run only, not production capacity.

| Metric | Measured | Budget | Result | Method |
|---|---:|---:|---|---|
"""
regressed = False
for name, value, budget, unit, method in metrics:
    status = 'PASS' if value <= budget else 'OVER BUDGET' if value <= 2 * budget else 'FAIL (>2x)'
    regressed |= value > 2 * budget
    text += f'| {name} | {value:.3f} {unit} | ≤ {budget} {unit} | {status} | {method} |\n'
    print(f'OFFLINE_BUDGET {name}: {value:.3f} {unit} {status}')
text += """
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
"""
pathlib.Path(output).write_text(text)
raise SystemExit(1 if regressed else 0)
