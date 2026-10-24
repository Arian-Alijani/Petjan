#!/usr/bin/env bash
# =============================================================================
# server-capacity.sh — weak-server capacity harness (G08, ARCH §19 gates T + I).
#
# Runs the xUnit-hosted CapacityDriver (tests/Animora.Server.Tests/Capacity) inside a
# cgroup-constrained container (default --cpus=2 --memory=2g = the 1–3 core / 2–4 GiB
# reference host) against a real PostgreSQL (>= 18 in CI). The driver hosts the in-tree
# server (WebApplicationFactory) and drives the two independent pipelines (ARCH §6):
#   * command-intake : POST /api/commands and /api/commands/batch
#   * state-pull     : /sync/manifest | /sync/batches | /sync/pages
# and records throughput, p50/p95/p99, peak process RSS and PostgreSQL connection counts.
#
# The artifact is PLANNING REFERENCE ONLY (A-RULE 035): recorded, never a guaranteed
# user count. Only mechanical properties are asserted (no unexpected 5xx, no pool
# exhaustion, bounded p99, readiness green at the end).
#
# Env:
#   ANIMORA_PG_TEST_CONNECTION  admin Npgsql connection string (CREATE DATABASE capable) — required
#   CPUS=2  MEMORY=2g           container limits
#   DOTNET_IMAGE                SDK image used for the constrained run (default mcr.microsoft.com/dotnet/sdk:10.0)
#   CAPACITY_ALLOW_UNCONSTRAINED=1  run on the host when Docker is unavailable (result is then NOT a valid reference)
# Output: artifacts/server-capacity.json, artifacts/server-capacity.md
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

: "${ANIMORA_PG_TEST_CONNECTION:?ANIMORA_PG_TEST_CONNECTION is required (PostgreSQL admin connection)}"
CPUS="${CPUS:-2}"
MEMORY="${MEMORY:-2g}"
IMAGE="${DOTNET_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}"
ART="$REPO_ROOT/artifacts"
mkdir -p "$ART"
JSON="$ART/server-capacity.json"
rm -f "$JSON"

TEST_PROJ="tests/Animora.Server.Tests/Animora.Server.Tests.csproj"
FILTER="FullyQualifiedName~Animora.Server.Tests.Capacity.CapacityDriver"

echo "── server-capacity: building test project (Release, unconstrained — build is not what we measure)"
dotnet build "$TEST_PROJ" -c Release --nologo -v minimal

export ANIMORA_REQUIRE_PG=1
export ANIMORA_CAPACITY_OUTPUT="$JSON"
export ANIMORA_CAPACITY_CPUS="$CPUS"
export ANIMORA_CAPACITY_MEMORY="$MEMORY"

if command -v docker >/dev/null 2>&1; then
  echo "── server-capacity: running driver in container (--cpus=$CPUS --memory=$MEMORY, image $IMAGE)"
  # Memory swap = memory: no swapping out of the limit. Host network: PostgreSQL service is on 127.0.0.1.
  docker run --rm --network host \
    --cpus="$CPUS" --memory="$MEMORY" --memory-swap="$MEMORY" \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
    -e ANIMORA_REQUIRE_PG -e ANIMORA_PG_TEST_CONNECTION \
    -e ANIMORA_CAPACITY_OUTPUT="/work/artifacts/server-capacity.json" \
    -e ANIMORA_CAPACITY_CPUS -e ANIMORA_CAPACITY_MEMORY \
    -e NUGET_PACKAGES=/work/.nuget-cache \
    -v "$REPO_ROOT:/work" -w /work \
    "$IMAGE" \
    dotnet test "$TEST_PROJ" -c Release --no-build --nologo -v minimal --filter "$FILTER"
elif [ "${CAPACITY_ALLOW_UNCONSTRAINED:-0}" = "1" ]; then
  echo "⚠ Docker unavailable — running UNCONSTRAINED; the result is not a valid weak-server reference" >&2
  export ANIMORA_CAPACITY_CPUS="unconstrained" ANIMORA_CAPACITY_MEMORY="unconstrained"
  dotnet test "$TEST_PROJ" -c Release --no-build --nologo -v minimal --filter "$FILTER"
else
  echo "✗ Docker is required for the cgroup-constrained run (set CAPACITY_ALLOW_UNCONSTRAINED=1 to override)" >&2
  exit 2
fi

[ -s "$JSON" ] || { echo "✗ driver produced no $JSON" >&2; exit 1; }

python3 - "$JSON" "$ART/server-capacity.md" <<'PY'
import json, sys
src, dst = sys.argv[1], sys.argv[2]
d = json.load(open(src))
lines = [
    "# Server capacity — PLANNING REFERENCE ONLY",
    "",
    "> Recorded measurement on the reference profile below. It is **not** a guaranteed user count (A-RULE 035).",
    "",
    f"- Profile: cpus={d.get('cpus')} memory={d.get('memory')} · PostgreSQL: {d.get('postgresVersion')}",
    f"- Peak server-process RSS: {d.get('peakRssMiB')} MiB · peak PostgreSQL connections: {d.get('peakPgConnections')} (bound {d.get('poolBound')})",
    f"- Unexpected 5xx: {d.get('unexpected5xx')} · readiness at end: {d.get('readinessAtEnd')}",
    "",
    "| Profile | Concurrency | Requests | Throughput (req/s) | p50 ms | p95 ms | p99 ms |",
    "|---|---:|---:|---:|---:|---:|---:|",
]
for p in d.get("profiles", []):
    lines.append("| {name} | {concurrency} | {requests} | {throughput:.1f} | {p50:.1f} | {p95:.1f} | {p99:.1f} |".format(**p))
open(dst, "w").write("\n".join(lines) + "\n")
print("\n".join(lines))
PY
echo "✓ capacity artifacts: $JSON, $ART/server-capacity.md (planning reference only)"
