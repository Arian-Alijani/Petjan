#!/usr/bin/env bash
# =============================================================================
# _projects.sh  —  single source of truth for Animora's project groupings
# =============================================================================
# Sourced by the CI scripts (and usable locally). Defines, in ONE place, which
# csproj belongs to which "scope" so the GitHub Actions workflow and the local
# scripts can never disagree about what "server" vs "desktop" vs "core" means.
#
# Mirrors the groupings documented in scripts/sandbox/AI_GUIDE.md and
# scripts/windows/*.ps1:
#   * SERVER track  = Animora.Server + its DB/licensing/admin siblings.
#   * DESKTOP track = Animora.Desktop (Avalonia app) — the Windows product.
#   * CORE          = the shared libraries used by both.
#
# Paths are repo-root-relative. Resolve them against "$REPO_ROOT" (exported by
# the caller or computed here as a fallback).
# =============================================================================

# Fallback repo-root resolution so this file is also useful standalone.
if [ -z "${REPO_ROOT:-}" ]; then
  _SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  REPO_ROOT="$(cd "$_SELF_DIR/../.." && pwd)"
  export REPO_ROOT
fi

SOLUTION="$REPO_ROOT/Animora.sln"
export SOLUTION

# -----------------------------------------------------------------------------
# Production (src/) projects, grouped by scope.
# -----------------------------------------------------------------------------
DESKTOP_PROJECTS=(
  "src/Animora.Desktop/Animora.Desktop.csproj"
)

# The server / database / licensing / admin surface (Track S).
SERVER_PROJECTS=(
  "src/Animora.Server/Animora.Server.csproj"
  "src/Animora.Persistence.PostgreSql/Animora.Persistence.PostgreSql.csproj"
  "src/Animora.Server.Licensing/Animora.Server.Licensing.csproj"
  "src/Animora.Web.Admin/Animora.Web.Admin.csproj"
)

# Shared libraries consumed by both desktop and server.
CORE_PROJECTS=(
  "src/Animora.Domain/Animora.Domain.csproj"
  "src/Animora.Application/Animora.Application.csproj"
  "src/Animora.Contracts/Animora.Contracts.csproj"
  "src/Animora.Contracts.Wire/Animora.Contracts.Wire.csproj"
  "src/Animora.Persistence.Sqlite/Animora.Persistence.Sqlite.csproj"
  "src/Animora.Infrastructure.Local/Animora.Infrastructure.Local.csproj"
  "src/Animora.Client.Services/Animora.Client.Services.csproj"
  "src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj"
  "src/Animora.Sync.Custom/Animora.Sync.Custom.csproj"
)

# -----------------------------------------------------------------------------
# Test (tests/) projects, grouped by scope.
# -----------------------------------------------------------------------------
# Desktop-dependent test projects (do NOT need PostgreSQL; cover the shared
# domain/application/local-persistence/sync behaviour the desktop app runs on).
DESKTOP_TEST_PROJECTS=(
  "tests/Animora.Domain.Tests/Animora.Domain.Tests.csproj"
  "tests/Animora.Application.Tests/Animora.Application.Tests.csproj"
  "tests/Animora.Persistence.Tests/Animora.Persistence.Tests.csproj"
  "tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj"
)

# Server-track test projects. Server.Tests has PostgreSQL-gated [PgFact] tests
# (PgFixture -> ANIMORA_PG_TEST_CONNECTION); Persistence/Sync are shared but are
# also exercised by the server-test flow (see scripts/sandbox/server-test.sh).
SERVER_TEST_PROJECTS=(
  "tests/Animora.Server.Tests/Animora.Server.Tests.csproj"
  "tests/Animora.Persistence.Tests/Animora.Persistence.Tests.csproj"
  "tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj"
)

export DESKTOP_PROJECTS SERVER_PROJECTS CORE_PROJECTS
export DESKTOP_TEST_PROJECTS SERVER_TEST_PROJECTS

# abspath <repo-relative-path> -> absolute path under REPO_ROOT.
abspath() { printf '%s/%s\n' "$REPO_ROOT" "$1"; }
