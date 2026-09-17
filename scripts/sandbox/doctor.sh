#!/usr/bin/env bash
# =============================================================================
# doctor.sh  —  one-shot health/constraints report for this sandbox
# =============================================================================
# Prints, in a compact form, everything the AI agent needs to decide what to do
# next: is the SDK present? is xvfb present? how much RAM/disk/tmpfs is free?
# Never fails the shell — purely informational.
#
#   scripts/sandbox/doctor.sh
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"

echo "===== Animora sandbox doctor ====="
printf '%-14s %s\n' "repo:"    "$REPO_ROOT"
printf '%-14s %s\n' "os:"      "$(. /etc/os-release; echo "$PRETTY_NAME") $(uname -m)"
printf '%-14s %s\n' "cpu:"     "$(nproc) cores"
printf '%-14s %s\n' "mem:"     "$(free -h | awk '/^Mem:/{print $7" avail / "$2" total"}')"
printf '%-14s %s\n' "disk(/):" "$(df -h / | awk 'NR==2{print $4" avail / "$2}')"
printf '%-14s %s\n' "tmpfs:"   "$(df -h /tmp | awk 'NR==2{print $4" avail / "$2" (KEEP SCRATCH OFF THIS)"}')"

echo "----- toolchain -----"
if has_dotnet; then
  printf '%-14s %s\n' "dotnet:" "$(dotnet --version)  ($DOTNET_ROOT)"
else
  printf '%-14s %s\n' "dotnet:" "MISSING -> run install-prereqs.sh"
fi
command -v Xvfb >/dev/null 2>&1 \
  && printf '%-14s %s\n' "xvfb:" "present (headless run OK)" \
  || printf '%-14s %s\n' "xvfb:" "MISSING (build/test OK, run needs it)"

echo "----- build output -----"
for c in Debug Release; do
  dll="$REPO_ROOT/$DESKTOP_DLL_REL/$c/net10.0/Animora.Desktop.dll"
  [ -f "$dll" ] && printf '%-14s %s\n' "$c:" "built" || printf '%-14s %s\n' "$c:" "-"
done

echo "----- scratch -----"
printf '%-14s %s\n' "nuget:"  "$NUGET_PACKAGES"
printf '%-14s %s\n' "logs:"   "$LOG_DIR"
[ -d "$LOG_DIR" ] && ls -1 "$LOG_DIR" 2>/dev/null | sed 's/^/               log: /' || true
echo "=================================="
