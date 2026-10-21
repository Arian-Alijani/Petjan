"""End-to-end pipeline: .bak -> reader -> tables -> mapping -> validated bundle."""
from __future__ import annotations

import struct
import sys
from pathlib import Path

from .bundle import load_schema, referential_errors, summarize, validate, write_bundle
from .mapping import build_bundle
from .mdf_reader import MdfError, MdfReader
from .tables import LegacyTables


def run_pipeline(bak_path: Path, out_path: Path, include_raw: bool = False, verbose: bool = False) -> int:
    """Run the conversion. Exit codes: 0 ok, 4 unreadable backup, 5 bundle failed validation."""
    try:
        reader = MdfReader.from_path(bak_path)
    except (MdfError, OSError) as exc:
        print(f"error: cannot read backup: {exc}", file=sys.stderr)
        return 4
    if verbose:
        print(f"MDF base {reader.base}: {reader.page_windows} page windows, {reader.data_pages} data pages, "
              f"{len(reader.tables)} tables")
    try:
        counts = reader.row_counts()
        tables = LegacyTables(reader)
        bundle = build_bundle(tables, reader.sha256, counts, include_raw=include_raw,
                              extra_warnings=sorted(set(reader.warnings)))
    except (MdfError, ValueError, IndexError, KeyError, struct.error) as exc:
        print(f"error: unsupported backup structure: {exc}", file=sys.stderr)
        return 4

    errors = validate(bundle, load_schema()) + referential_errors(bundle)
    if errors:
        print("error: generated bundle failed validation (nothing written):", file=sys.stderr)
        for message in errors[:20]:
            print(f"  - {message}", file=sys.stderr)
        return 5
    write_bundle(bundle, out_path)
    print(summarize(bundle, verbose))
    print(f"\nWrote {out_path} ({out_path.stat().st_size / 1024:.0f} KiB). "
          "It contains personal data - upload it in the admin panel and do not commit it.")
    return 0
