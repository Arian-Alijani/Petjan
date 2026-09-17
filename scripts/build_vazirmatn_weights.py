#!/usr/bin/env python3
"""
Fetch + verify the bundled static Vazirmatn weight files so Avalonia's font
matcher resolves EVERY requested FontWeight (Regular..Black) to real outlines on
ALL platforms — Windows (DirectWrite/GDI), macOS (CoreText) and Linux
(FreeType/fontconfig) — instead of rendering heavy weights as solid black
rectangles (.notdef "tofu").

================================================================================
Bug history / root cause  (READ THIS BEFORE TOUCHING FONT NAMING)
================================================================================
Three attempts were made; the first two shipped and STILL showed black boxes for
bold headings on real Windows machines even though headless Linux screenshots
looked fine. This is the crucial trap: the Linux FreeType/fontconfig matcher
weight-resolves by OS/2.usWeightClass and is forgiving, so a broken naming table
renders correctly under Xvfb in the sandbox — while Windows DirectWrite/GDI,
which strictly honours the legacy RIBBI (Regular/Italic/Bold/BoldItalic) model,
drops the duplicates and falls back to .notdef. Never validate a font-naming fix
on Linux alone.

  Attempt 1 — use the upstream VARIABLE font (single Vazirmatn.ttf, wght 100..900).
    Avalonia cannot weight-resolve a variable font, so every non-Regular label
    rendered as tofu. -> switched to discrete static weights.

  Attempt 2 — static weights, but each heavy file kept its OWN legacy family
    name (ID1 = "Vazirmatn ExtraBold", ...) while leftover variable tables
    (STAT weight axis, fvar named-instances, nameID >= 256) were still present.
    The matcher couldn't collapse the six disagreeing families into one, so the
    heavy weights still fell back to tofu.

  Attempt 3 (the regression this fix replaces) — forced ALL six files to
    ID1 = "Vazirmatn" and ID2 = a RIBBI subfamily of only "Regular"/"Bold".
    That created a LEGACY-FAMILY COLLISION:
        family="Vazirmatn" subfamily="Regular"  <- Regular 400, Medium 500, SemiBold 600
        family="Vazirmatn" subfamily="Bold"     <- Bold 700, ExtraBold 800, Black 900
    The RIBBI model allows only ONE face per (family, subfamily) pair. Linux
    kept all six (weight-class match) so the sandbox looked fixed; Windows
    DirectWrite kept exactly one "Bold" face (700) and one "Regular" face,
    discarding Medium/SemiBold/ExtraBold/Black. Requesting ExtraBold(800)/
    Black(900) then found no face -> .notdef -> black rectangles. THAT is why
    the headings were boxes on your machine but not in the sandbox.

================================================================================
The canonical fix (this script) — the Google Fonts static-family model
================================================================================
Use the UPSTREAM official static TTFs verbatim. Their naming is the correct,
cross-platform "preferred family + RIBBI base pair" layout:

    file        ID1 (legacy family)   ID2 (RIBBI)  ID16 (preferred)  ID17
    ---------   -------------------   -----------  ----------------  ---------
    Regular     Vazirmatn             Regular      (absent)          (absent)
    Bold        Vazirmatn             Bold         (absent)          (absent)
    Medium      Vazirmatn Medium      Regular      Vazirmatn         Medium
    SemiBold    Vazirmatn SemiBold    Regular      Vazirmatn         SemiBold
    ExtraBold   Vazirmatn ExtraBold   Regular      Vazirmatn         ExtraBold
    Black       Vazirmatn Black       Regular      Vazirmatn         Black

Why this resolves everywhere:
  * The legacy table is a valid RIBBI pair (Vazirmatn / Regular + Bold) with NO
    duplicate (family, subfamily) rows, so DirectWrite/GDI keep every face.
  * Medium/SemiBold/ExtraBold/Black each carry a UNIQUE legacy family name, so
    they never collide with anything and register as their own faces.
  * ID16 = "Vazirmatn" (preferred/typographic family) ties all six together, so
    a matcher that understands the preferred family sees one "Vazirmatn" with
    six weights; one that doesn't still finds each weight via its legacy name.
  * No leftover variable tables (STAT/fvar/gvar/avar/HVAR/VVAR/MVAR); the only
    nameID >= 256 record is the harmless "Farsi Digits" stylistic-set label.

Avalonia's `avares://.../Assets/Fonts#Vazirmatn` URI matches on the preferred
family, and each `FontWeight` (400/500/600/700/800/900) binds to the file whose
OS/2.usWeightClass matches — so ExtraBold headings render real Persian glyphs.

DO NOT rewrite ID1 to a single shared "Vazirmatn" for the heavy weights again:
that is exactly the Attempt-3 collision that broke Windows.

--------------------------------------------------------------------------------
Usage
--------------------------------------------------------------------------------
  python3 scripts/build_vazirmatn_weights.py            # verify bundled files
  python3 scripts/build_vazirmatn_weights.py --fetch     # re-download from upstream

The upstream release is Vazirmatn (rastikerdar/vazirmatn), OFL-1.1. `--fetch`
overwrites the six bundled TTFs with the official static faces; the default run
only VERIFIES that the bundled files still have the correct, collision-free
naming (fails loudly if a future edit reintroduces the bug).
"""
from __future__ import annotations

import argparse
import sys
import urllib.request
from pathlib import Path

from fontTools.ttLib import TTFont

FONTS_DIR = Path("src/Animora.Desktop/Assets/Fonts")
PREFERRED_FAMILY = "Vazirmatn"
UPSTREAM = (
    "https://github.com/rastikerdar/vazirmatn/raw/master/fonts/ttf/Vazirmatn-{style}.ttf"
)

# style -> expected OS/2.usWeightClass
WEIGHTS = {
    "Regular": 400,
    "Medium": 500,
    "SemiBold": 600,
    "Bold": 700,
    "ExtraBold": 800,
    "Black": 900,
}

# Variable-font tables that must never be present in a static instance.
VAR_TABLES = ("STAT", "fvar", "gvar", "avar", "HVAR", "VVAR", "MVAR", "cvar")


def _name(font: TTFont, nid: int) -> str | None:
    rec = font["name"].getName(nid, 3, 1, 0x409) or font["name"].getName(nid, 1, 0, 0)
    return rec.toUnicode() if rec else None


def fetch() -> None:
    FONTS_DIR.mkdir(parents=True, exist_ok=True)
    for style in WEIGHTS:
        url = UPSTREAM.format(style=style)
        dst = FONTS_DIR / f"Vazirmatn-{style}.ttf"
        print(f"  downloading {style:10s} <- {url}")
        with urllib.request.urlopen(url) as resp:
            dst.write_bytes(resp.read())
    print("Fetched all six official static faces.")


def verify() -> bool:
    """Return True iff all six files are the correct, collision-free static faces.

    Enforces the invariants that keep heavy weights from becoming Windows tofu:
      - exactly the expected usWeightClass per file
      - a valid RIBBI base pair (Regular + Bold under legacy family "Vazirmatn")
        with NO extra file re-using that legacy family (the Attempt-3 collision)
      - preferred family (ID16) == "Vazirmatn" for the non-RIBBI weights
      - no leftover variable-font tables
    """
    ok = True
    legacy_family_users: dict[str, list[str]] = {}

    for style, expected_w in WEIGHTS.items():
        path = FONTS_DIR / f"Vazirmatn-{style}.ttf"
        if not path.exists():
            print(f"  MISSING: {path}")
            ok = False
            continue

        font = TTFont(str(path))
        os2_w = font["OS/2"].usWeightClass
        id1 = _name(font, 1)
        id2 = _name(font, 2)
        id16 = _name(font, 16)
        var_left = [t for t in VAR_TABLES if t in font]

        legacy_family_users.setdefault((id1, id2), []).append(style)

        problems = []
        if os2_w != expected_w:
            problems.append(f"usWeightClass={os2_w} (expected {expected_w})")
        if var_left:
            problems.append(f"variable tables left: {var_left}")

        if style in ("Regular", "Bold"):
            # RIBBI base pair: legacy family must be the bare preferred family.
            if id1 != PREFERRED_FAMILY:
                problems.append(f"ID1={id1!r} (expected {PREFERRED_FAMILY!r})")
            if id2 != style:
                problems.append(f"ID2={id2!r} (expected {style!r})")
        else:
            # Non-RIBBI weight: needs a UNIQUE legacy family + preferred=Vazirmatn.
            if id1 == PREFERRED_FAMILY:
                problems.append(
                    f"ID1={id1!r} collides with the RIBBI base family "
                    f"(must be a unique 'Vazirmatn {style}')"
                )
            if id16 != PREFERRED_FAMILY:
                problems.append(f"ID16={id16!r} (expected {PREFERRED_FAMILY!r})")

        status = "OK" if not problems else "FAIL"
        print(
            f"  [{status}] {path.name:24s} w={os2_w} ID1={id1!r} ID2={id2!r} ID16={id16!r}"
        )
        for p in problems:
            print(f"         - {p}")
            ok = False

    # Legacy-family collision guard: the (family, subfamily) pairs used by the
    # RIBBI base must be claimed by exactly one file each. This is the exact
    # invariant whose violation caused the Windows-only black-box regression.
    for (fam, sub), users in legacy_family_users.items():
        if fam == PREFERRED_FAMILY and len(users) > 1:
            print(
                f"  COLLISION: legacy family ({fam!r},{sub!r}) claimed by "
                f"{users} — DirectWrite will drop all but one -> tofu."
            )
            ok = False

    return ok


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--fetch",
        action="store_true",
        help="re-download the six official static faces from upstream first",
    )
    args = ap.parse_args()

    if args.fetch:
        print("Fetching official static Vazirmatn faces ...")
        fetch()

    print(f"Verifying static Vazirmatn weights in {FONTS_DIR} ...")
    if verify():
        print("OK: all six files form a valid, collision-free 'Vazirmatn' family.")
        return 0
    print("ERROR: font naming is broken — heavy weights will render as tofu.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
