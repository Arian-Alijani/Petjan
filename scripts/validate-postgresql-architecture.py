#!/usr/bin/env python3
"""Static architecture regression checks, NOT PostgreSQL/runtime acceptance.

Run: python3 -B scripts/validate-postgresql-architecture.py
Uses only the standard library; reads current design and forward D/E phases.
Historical logs, binary sample imports and generated UI references are excluded.
Real database, provider, migration, load and restore tests remain release gates.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
PHASE_ROOT = ROOT / "backend-roadmap" / "phases"
FORWARD = sorted(PHASE_ROOT.glob("PHASE-D*.md")) + sorted(PHASE_ROOT.glob("PHASE-E*.md"))
DESIGN = [ROOT / "ARCHITECTURE.md", ROOT / "STACK.md",
          ROOT / "backend-roadmap/CONVENTIONS.md", ROOT / "backend-roadmap/ROADMAP.md"]


def read(path):
    return path.read_text(encoding="utf-8")


def phase(prefix):
    matches = list(PHASE_ROOT.glob(f"PHASE-{prefix}-*.md"))
    if len(matches) != 1:
        raise AssertionError(f"Expected one {prefix} phase, got {len(matches)}")
    return read(matches[0])


class PostgreSqlArchitectureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.arch = read(ROOT / "ARCHITECTURE.md")
        cls.stack = read(ROOT / "STACK.md")

    def test_no_executable_sql_server_recipes_in_current_design(self):
        obsolete = re.compile(
            r"Microsoft\.EntityFrameworkCore\.SqlServer|Animora\.Persistence\.SqlServer|"
            r"\bSqlServerAvailability\b|\bHasSqlServer\b|\bCHANGETABLE\b|"
            r"CHANGE_TRACKING_|\bUPDLOCK\b|\bREADPAST\b|\bnvarchar\b|"
            r"\bdatetimeoffset(?:2)?\s*\(|DATA_COMPRESSION\s*=|trace flag 11042|"
            r"\bMonotonicStampService\b|PostgreSQL Express|PostgreSQL.*baseline = Standard"
        )
        for path in DESIGN + FORWARD:
            with self.subTest(path=path.relative_to(ROOT)):
                self.assertIsNone(obsolete.search(read(path)))

    def test_every_forward_phase_binds_canonical_postgresql_contract(self):
        self.assertEqual(22, len(FORWARD))
        for path in FORWARD:
            with self.subTest(path=path.name):
                text = read(path)
                self.assertIn("PostgreSQL", text)
                self.assertIn("§PG", text)
                self.assertIn("## Acceptance criteria", text)
                self.assertIn("## PROGRESS.md update", text)

    def test_post_intake_phases_do_not_reset_v2_command_version(self):
        for path in FORWARD:
            if path.name.split("-")[1] not in ("D01", "D02", "D03", "D04"):
                self.assertNotIn("`WireContract.Version` stays `1`", read(path), path.name)
        self.assertIn("**Depends on:** D05, D07", phase("D08"))

    def test_markdown_code_fences_are_balanced(self):
        for path in DESIGN + FORWARD:
            with self.subTest(path=path.name):
                fences = re.findall(r"^\s*```", read(path), re.MULTILINE)
                self.assertEqual(0, len(fences) % 2)

    def test_postgresql_sections_keep_existing_cross_references(self):
        for title in ("## §16-CT.", "## §17-CT.", "## §18-CT.", "## §PG."):
            headings = re.findall(r"^" + re.escape(title) + r" ", self.arch, re.MULTILINE)
            self.assertEqual(1, len(headings))
        for part in range(1, 9):
            self.assertIn(f"### §PG.{part}", self.arch)

    def test_provider_versions_match_declared_dependency_range(self):
        for text in (self.arch, self.stack, phase("D01")):
            self.assertIn("10.0.3", text)
        self.assertIn("[10.0.4,11.0.0)", self.arch)
        self.assertIn("10.0.12", read(ROOT / "Directory.Packages.props"))
        self.assertIn("UseNpgsql", phase("D01"))

    def test_commit_safe_counter_is_not_a_native_sequence_cursor(self):
        critical = self.arch.split("## §16-CT.", 1)[1].split("## §19-tomb.", 1)[0]
        for term in ("TenantFeedState", "LastSequence=LastSequence+1", "FOR UPDATE",
                     "Rollback cancels", "through commit", "Different tenants"):
            self.assertIn(term, critical)
        for rejected in ("BIGSERIAL", "nextval()", "xmin", "WAL LSN", "block reservations"):
            self.assertIn(rejected, critical)
        self.assertIn("T2 cannot commit a higher position first", phase("D08"))

    def test_snapshot_and_pages_do_not_requery_live_state(self):
        for text in (self.arch, phase("D08")):
            self.assertIn("REPEATABLE READ", text)
            self.assertIn("SAME snapshot", text)
        self.assertIn("NEVER by rereading changing base tables", self.arch)
        self.assertIn("immutable", phase("D08").lower())
        self.assertIn("256 KiB", phase("D08"))

    def test_uuid_tokens_use_existing_domain_string_converter(self):
        self.assertIn("public string? Version", read(ROOT / "src/Animora.Domain/Primitives/SyncableEntity.cs"))
        for text in (self.arch, self.stack, phase("D02")):
            self.assertIn("converter", text)
            self.assertIn("IsConcurrencyToken()", text)
            self.assertIn("RETURNING", text)
        self.assertIn("gen_random_uuid()", self.arch)

    def test_numeric_v1_wire_seam_is_explicitly_versioned_not_cast(self):
        wire = read(ROOT / "src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs")
        if "long? ExpectedAggregateVersion" in wire:
            for text in (self.arch, phase("D05"), phase("E06")):
                self.assertIn("ExpectedServerVersion", text)
                self.assertIn("V1", text)
            self.assertIn("WireContract **v2**", self.arch)
            self.assertIn("AppliedServerVersion", phase("D05"))
            self.assertIn("numeric-to-UUID", phase("D05"))

    def test_critical_client_is_custom_and_checkpoint_atomic(self):
        text = phase("E03")
        for term in ("Animora.Sync.Custom", "ONE transaction", "FromVersion", "overlay",
                     "UseTransaction", "invisible", "cipher", "NullSyncEngine"):
            self.assertIn(term, text)
        self.assertNotIn("Animora.Sync.Datasync", text)
        self.assertNotIn("commits per entity", phase("E04"))
        self.assertIn("not independent entity commits", phase("E04"))

    def test_shadow_recovery_preserves_pending_intent(self):
        for text in (phase("E03"), phase("E05")):
            self.assertIn("shadow", text)
            self.assertIn("active generation", text)
            self.assertIn("pending", text)
        self.assertIn("never delete it or pending operations", phase("E03"))
        self.assertIn("blocks unsafe shadow switch", phase("E05"))

    def test_retention_floor_and_deletes_are_atomic_and_tenant_bound(self):
        for term in ("FirstSeenCommittedAtUtc", "contiguous eligible prefix",
                     "MinValidSequence=K", "graveyard", "tenant-qualified"):
            self.assertIn(term, self.arch)
        self.assertIn("offline window+15 days", phase("D08"))
        self.assertIn("GC crash atomicity", phase("D08"))

    def test_outbox_claims_reclaim_expired_work_and_fence_completion(self):
        text = phase("D09")
        for term in ("FOR UPDATE SKIP LOCKED", "RETURNING o.*", "@claimed, @processing",
                     "ClaimToken", "unexpired lease", "clock_timestamp()"):
            self.assertIn(term, text)
        self.assertIn("stable dedup keys", text)
        self.assertIn("ON CONFLICT", phase("D05"))
        self.assertIn("subsequent READ COMMITTED lookup", phase("D05"))

    def test_small_host_has_combined_not_per_tenant_pool_budget(self):
        for term in ("2 GiB / 1 core", "4 GiB / 2–3 cores", "12 = interactive 8",
                     "20 = interactive 14", "work_mem", "max_parallel_workers_per_gather"):
            self.assertIn(term, self.arch)
        self.assertNotIn("default 60", phase("D13"))
        self.assertIn("MinPoolSize=0", phase("D13"))
        self.assertNotIn("4 vCPU / 16 GB", self.stack)

    def test_security_and_restore_use_postgresql_primitives(self):
        for term in ("set_config", "current_setting", "BYPASSRLS", "VerifyFull",
                     "pg_basebackup", "pg_verifybackup", "pg_amcheck", "PITR",
                     "fsync=on", "synchronous_commit=on", "SECURITY DEFINER"):
            self.assertIn(term, self.arch)
        self.assertIn("missing DB is FAIL", phase("E08"))
        self.assertIn("fresh ServerFeedEpoch", phase("E09"))

    def test_forward_requirements_and_implementation_status_stay_honest(self):
        lock = read(ROOT / "backend-roadmap/00-REQUIREMENTS-LOCK.md")
        defined = set(re.findall(r"\| (R-B\d+) \|", lock))
        self.assertIn("1–3 cores / 2–4 GiB", lock)
        for path in FORWARD:
            header = read(path).split("## Read before starting", 1)[0]
            for requirement in re.findall(r"R-B\d+", header):
                self.assertIn(requirement, defined, path.name)
        progress = read(ROOT / "backend-roadmap/PROGRESS.md").split("## Phase status", 1)[0]
        self.assertIn("no D/E phase is marked done", progress)
        self.assertIn("Next phase: `D01`", progress)
        self.assertIn("NO server", self.arch)


if __name__ == "__main__":
    unittest.main(verbosity=2)
