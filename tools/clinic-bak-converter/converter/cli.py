"""Command-line entry point for the Clinic.bak converter.

Usage (cross-platform)::

    python -m converter --bak path/to/Clinic.bak --out clinic-import.animora.json

On Windows, double-click ``run-converter.cmd`` which prompts for the paths and
invokes this module. The converter is stdlib-only; no packages are required.

This module is intentionally thin: it parses arguments, wires the pipeline
(reader -> extractors -> mapping -> bundle writer) and prints a human summary.
The heavy lifting lives in the sibling modules, added in later tasks:
  * ``mdf_reader``  - raw MDF page/record reader (T3)
  * ``tables``      - typed per-table extractors (T4)
  * ``convert``     - value converters: Jalali dates, digits, enums, money (T5)
  * ``mapping``     - A->B mapping engine, owner dedupe, lossless passthrough (T6)
  * ``bundle``      - serialize + JSON-Schema validation + summary (T7)
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import BUNDLE_FORMAT_VERSION, __version__


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="clinic-bak-converter",
        description=(
            "Convert a legacy veterinary Clinic.bak (SQL Server backup) into an "
            "Animora clinic import bundle (clinic-import.animora.json)."
        ),
    )
    parser.add_argument(
        "--bak",
        required=True,
        type=Path,
        help="Path to the input Clinic.bak file (SQL Server native backup).",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=Path("clinic-import.animora.json"),
        help="Path to write the output bundle (default: ./clinic-import.animora.json).",
    )
    parser.add_argument(
        "--raw",
        action="store_true",
        help=(
            "Include a per-table raw passthrough section in the bundle for full "
            "losslessness/debugging (larger file; contains PII - keep local)."
        ),
    )
    parser.add_argument(
        "--verbose",
        "-v",
        action="store_true",
        help="Print per-table row counts and detailed warnings.",
    )
    parser.add_argument(
        "--version",
        action="version",
        version=f"%(prog)s {__version__} (bundle format v{BUNDLE_FORMAT_VERSION})",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)

    if not args.bak.exists():
        print(f"error: input backup not found: {args.bak}", file=sys.stderr)
        return 2

    # Pipeline is wired incrementally across tasks T3-T7. Until those modules
    # land, fail loudly rather than pretend to have produced a bundle.
    try:
        from .pipeline import run_pipeline  # local import: added in T7
    except ImportError:
        print(
            "error: conversion pipeline is not implemented yet in this build "
            "(reader/mapping/bundle modules land in tasks T3-T7).",
            file=sys.stderr,
        )
        return 3

    return run_pipeline(
        bak_path=args.bak,
        out_path=args.out,
        include_raw=args.raw,
        verbose=args.verbose,
    )


if __name__ == "__main__":
    raise SystemExit(main())
