#!/usr/bin/env bash
# =============================================================================
# make-release-manifest.sh — release manifest for a BUILT output (G09, STACK §16, ARCH §17.3).
#
# Against a built/published output directory it generates, with NO cloud call:
#   (a) a NuGet SBOM (CycloneDX 1.5 JSON) from the restore graph (obj/project.assets.json of the
#       given project) — offline, deterministic, first-party (no external tool download);
#   (b) a native-binary inventory JSON — every native artifact (*.dll that is NOT a managed assembly,
#       *.so, *.dylib, e_sqlite3 / e_sqlite3mc / SQLCipher …) with sha256, version and RID
#       (a NuGet SBOM alone is insufficient for native code);
#   (c) a suppression-ledger validation — schema requires
#       advisoryId, justification, affectedPackage, compensatingControl, expiry, owner;
#       the script EXITS NON-ZERO if any native-DB/crypto entry (sqlcipher / e_sqlite3 / e_sqlite3mc)
#       lacks an expiry (or is already expired) — no permanent suppression.
#
# It also asserts the supply-chain switches that already exist in the tree (read-only; it never edits
# Directory.Build.props / Directory.Packages.props): NuGetAuditMode=all is REQUIRED; lock-file mode
# (RestorePackagesWithLockFile) is reported, and REQUIRED only with MANIFEST_REQUIRE_LOCKFILE=1.
#
# Usage:
#   scripts/release/make-release-manifest.sh --output-dir <built-output> [--project <csproj>]
#                                            [--rid <win-x64|…>] [--out <dir>] [--ledger <file>]
#   scripts/release/make-release-manifest.sh --self-test      # validates the ledger rules only
#
# Defaults: --project src/Animora.Desktop/Animora.Desktop.csproj   --out artifacts/release-manifest
#           --ledger scripts/release/suppression-ledger.json
# Outputs : <out>/sbom.cdx.json  <out>/native-inventory.json  <out>/suppression-ledger.validated.json
# Exit    : 0 ok · 1 violation (audit mode off / ledger rule broken / required native DB missing) · 2 usage
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

OUTPUT_DIR=""; PROJECT="src/Animora.Desktop/Animora.Desktop.csproj"; RID=""
OUT="artifacts/release-manifest"; LEDGER="scripts/release/suppression-ledger.json"; SELF_TEST=0
while [ $# -gt 0 ]; do
  case "$1" in
    --output-dir) OUTPUT_DIR="$2"; shift 2 ;;
    --project)    PROJECT="$2"; shift 2 ;;
    --rid)        RID="$2"; shift 2 ;;
    --out)        OUT="$2"; shift 2 ;;
    --ledger)     LEDGER="$2"; shift 2 ;;
    --self-test)  SELF_TEST=1; shift ;;
    -h|--help)    sed -n '2,29p' "$0"; exit 0 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done
command -v python3 >/dev/null 2>&1 || { echo "✗ python3 is required" >&2; exit 2; }

# ── supply-chain switches (read-only) ────────────────────────────────────────
check_switches() {
  local audit lock
  audit="$(grep -hoE '<NuGetAuditMode>[^<]+' Directory.Build.props Directory.Packages.props 2>/dev/null | head -1 | sed 's/.*>//')"
  lock="$(grep -hoE '<RestorePackagesWithLockFile>[^<]+' Directory.Build.props Directory.Packages.props 2>/dev/null | head -1 | sed 's/.*>//')"
  echo "supply-chain: NuGetAuditMode=${audit:-<unset>}  RestorePackagesWithLockFile=${lock:-<unset>}"
  if [ "${audit,,}" != "all" ]; then echo "✗ NuGetAuditMode must be 'all' (Directory.Build.props)"; return 1; fi
  if [ "${lock,,}" != "true" ]; then
    if [ "${MANIFEST_REQUIRE_LOCKFILE:-0}" = "1" ]; then echo "✗ lock-file mode is required but RestorePackagesWithLockFile is not true"; return 1; fi
    echo "⚠ lock-file mode is OFF in the tree (reported, not edited here; set MANIFEST_REQUIRE_LOCKFILE=1 to make it fatal)"
  fi
}

# ── (c) suppression ledger ───────────────────────────────────────────────────
validate_ledger() { # validate_ledger <ledger.json> <out.json>
python3 - "$1" "$2" <<'PY'
import json, re, sys, datetime
src, dst = sys.argv[1], sys.argv[2]
REQUIRED = ["advisoryId", "justification", "affectedPackage", "compensatingControl", "expiry", "owner"]
NATIVE = re.compile(r"sqlcipher|e_sqlite3mc|e_sqlite3", re.I)
errors = []
try:
    doc = json.load(open(src, encoding="utf-8"))
except FileNotFoundError:
    doc = {"entries": []}            # no ledger file == no suppressions (allowed)
except json.JSONDecodeError as e:
    print(f"✗ ledger is not valid JSON: {e}"); sys.exit(1)
entries = doc.get("entries", doc if isinstance(doc, list) else [])
today = datetime.date.today()
for i, e in enumerate(entries):
    tag = f"entry[{i}] ({e.get('advisoryId', '?')})"
    for k in REQUIRED:
        v = e.get(k)
        if k != "expiry" and (v is None or str(v).strip() == ""):
            errors.append(f"{tag}: missing required field '{k}'")
    pkg = str(e.get("affectedPackage", ""))
    exp = e.get("expiry")
    is_native = bool(NATIVE.search(pkg))
    if exp in (None, "", "never", "none", "permanent"):
        errors.append(f"{tag}: " + ("native-DB/crypto suppression" if is_native else "suppression") + " has no expiry — permanent suppression is forbidden"
                      if is_native else f"{tag}: missing required field 'expiry'")
    else:
        try:
            d = datetime.date.fromisoformat(str(exp))
            if d < today:
                errors.append(f"{tag}: expiry {exp} is in the past — renew with a fresh justification or remove")
            elif is_native and (d - today).days > 366:
                errors.append(f"{tag}: native-DB/crypto expiry {exp} is more than 12 months out")
        except ValueError:
            errors.append(f"{tag}: expiry '{exp}' is not an ISO date (YYYY-MM-DD)")
result = {"schema": REQUIRED, "entryCount": len(entries), "valid": not errors, "errors": errors}
json.dump(result, open(dst, "w"), indent=2)
if errors:
    print("✗ suppression ledger violations:"); [print("  - " + m) for m in errors]; sys.exit(1)
print(f"✓ suppression ledger valid ({len(entries)} entr{'y' if len(entries)==1 else 'ies'}, native-DB/crypto all expiring)")
PY
}

mkdir -p "$OUT"

if [ "$SELF_TEST" = "1" ]; then
  T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
  echo '{"entries":[{"advisoryId":"GHSA-x","justification":"j","affectedPackage":"SQLitePCLRaw.lib.e_sqlite3","compensatingControl":"c","expiry":"","owner":"o"}]}' >"$T/bad.json"
  if validate_ledger "$T/bad.json" "$T/out.json" >/dev/null; then echo "✗ self-test: native entry without expiry was ACCEPTED"; exit 1; fi
  echo '{"entries":[]}' >"$T/ok.json"; validate_ledger "$T/ok.json" "$T/out.json" >/dev/null
  echo "✓ self-test: ledger rules enforce native-DB/crypto expiry"; exit 0
fi

[ -n "$OUTPUT_DIR" ] || { echo "✗ --output-dir is required" >&2; exit 2; }
[ -d "$OUTPUT_DIR" ] || { echo "✗ output dir not found: $OUTPUT_DIR (build/publish first)" >&2; exit 2; }

FAIL=0
check_switches || FAIL=1
validate_ledger "$LEDGER" "$OUT/suppression-ledger.validated.json" || FAIL=1

# ── (a) SBOM + (b) native inventory ──────────────────────────────────────────
ASSETS="$(dirname "$PROJECT")/obj/project.assets.json"
python3 - "$OUTPUT_DIR" "$ASSETS" "$RID" "$OUT" "$PROJECT" <<'PY' || FAIL=1
import hashlib, json, os, re, sys, datetime
out_dir, assets_path, rid_arg, out, project = sys.argv[1:6]
now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

# --- package graph from the restore assets (offline) --------------------------------------------
packages, native_by_path = {}, {}
if os.path.isfile(assets_path):
    assets = json.load(open(assets_path, encoding="utf-8"))
    for key, lib in assets.get("libraries", {}).items():
        if lib.get("type") == "package":
            name, _, ver = key.partition("/")
            packages[name.lower()] = (name, ver, lib.get("sha512", ""))
    for tfm in assets.get("targets", {}).values():
        for key, node in tfm.items():
            name, _, ver = key.partition("/")
            for p, meta in (node.get("runtimeTargets") or {}).items():
                if meta.get("assetType") == "native":
                    native_by_path[os.path.basename(p).lower()] = (name, ver, meta.get("rid", ""))
            for p in (node.get("native") or {}):
                native_by_path.setdefault(os.path.basename(p).lower(), (name, ver, ""))
else:
    print(f"⚠ {assets_path} not found — SBOM will list no packages (restore the project first)")

components = []
for name, ver, sha in sorted(packages.values(), key=lambda t: (t[0].lower(), t[1])):
    c = {"type": "library", "name": name, "version": ver, "purl": f"pkg:nuget/{name}@{ver}", "bom-ref": f"pkg:nuget/{name}@{ver}"}
    if sha:
        try:
            import base64
            c["hashes"] = [{"alg": "SHA-512", "content": base64.b64decode(sha).hex()}]
        except Exception:
            pass
    components.append(c)
sbom = {
    "bomFormat": "CycloneDX", "specVersion": "1.5", "version": 1,
    "metadata": {"timestamp": now, "component": {"type": "application", "name": os.path.splitext(os.path.basename(project))[0]},
                 "tools": [{"vendor": "Animora", "name": "make-release-manifest.sh"}]},
    "components": components,
}
json.dump(sbom, open(os.path.join(out, "sbom.cdx.json"), "w"), indent=2)

# --- native inventory ----------------------------------------------------------------------------
NATIVE_EXT = (".so", ".dylib")
DB_CRYPTO = re.compile(r"sqlcipher|e_sqlite3mc|e_sqlite3|sqlite3", re.I)
def is_managed(path):
    try:
        with open(path, "rb") as f:
            head = f.read(4)
            if head[:2] != b"MZ":
                return False
            f.seek(0); return b"BSJB" in f.read()          # CLR metadata signature
    except OSError:
        return False
def rid_from_path(path):
    m = re.search(r"runtimes[\\/]([^\\/]+)[\\/]native", path)
    return m.group(1) if m else ""
def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

inv = []
for root, _, files in os.walk(out_dir):
    for fn in sorted(files):
        p = os.path.join(root, fn); low = fn.lower()
        native = low.endswith(NATIVE_EXT) or (low.endswith(".dll") and not is_managed(p)) or low.endswith(".exe") and not is_managed(p)
        if not native:
            continue
        pkg = native_by_path.get(low)
        rid = (pkg[2] if pkg and pkg[2] else "") or rid_from_path(p) or rid_arg
        inv.append({
            "file": os.path.relpath(p, out_dir).replace("\\", "/"),
            "sha256": sha256(p), "sizeBytes": os.path.getsize(p),
            "package": pkg[0] if pkg else None,
            "version": pkg[1] if pkg else "unknown",
            "rid": rid or "unknown",
            "category": "database-crypto" if DB_CRYPTO.search(fn) or (pkg and DB_CRYPTO.search(pkg[0])) else "other-native",
        })
inv.sort(key=lambda e: e["file"])
doc = {"generated": now, "outputDir": out_dir, "count": len(inv),
       "databaseCrypto": [e["file"] for e in inv if e["category"] == "database-crypto"], "artifacts": inv}
json.dump(doc, open(os.path.join(out, "native-inventory.json"), "w"), indent=2)
print(f"✓ SBOM: {len(components)} NuGet component(s) -> {out}/sbom.cdx.json")
print(f"✓ native inventory: {len(inv)} native artifact(s), {len(doc['databaseCrypto'])} DB/crypto -> {out}/native-inventory.json")
if os.environ.get("MANIFEST_REQUIRE_NATIVE_DB") == "1" and not doc["databaseCrypto"]:
    print("✗ MANIFEST_REQUIRE_NATIVE_DB=1 but no e_sqlite3/e_sqlite3mc/SQLCipher binary was found in the output"); sys.exit(1)
PY

if [ "$FAIL" -ne 0 ]; then echo "✗ RELEASE MANIFEST: violations found"; exit 1; fi
echo "✓ RELEASE MANIFEST: complete ($OUT)"
