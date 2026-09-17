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

The upstream files are valid, but that alone does NOT guarantee Avalonia will
select them. A directory URI such as `Assets/Fonts#Vazirmatn` asks Avalonia/Skia
to group files whose legacy names (ID1) differ by using their typographic family
(ID16). That grouping produced different results on Windows and Linux. The app
therefore uses one file URI per weight, with the exact ID1 after `#`, and maps
effective FontWeight values to those resources in App.axaml. This script verifies
both halves of that invariant: font metadata and the XAML file-to-family mapping.

DO NOT rewrite ID1 to one shared "Vazirmatn", and do not replace the per-file
resources with one directory URI. Both earlier approaches reintroduce ambiguity.

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
import xml.etree.ElementTree as ET

from fontTools.ttLib import TTFont

FONTS_DIR = Path("src/Animora.Desktop/Assets/Fonts")
TYPOGRAPHY_XAML = Path("src/Animora.Desktop/Resources/Theme/Typography.axaml")
APP_XAML = Path("src/Animora.Desktop/App.axaml")
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
PERSIAN_PROBE = "نبض مالی کلینیک بیماران فعال ۱۲۳۴۵۶۷۸۹۰"


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


def verify_xaml_mapping(legacy_families: dict[str, str]) -> bool:
    """Ensure every weight uses its own file URI and exact internal ID1."""
    try:
        root = ET.parse(TYPOGRAPHY_XAML).getroot()
    except (OSError, ET.ParseError) as exc:
        print(f"  FAIL: cannot parse {TYPOGRAPHY_XAML}: {exc}")
        return False

    xaml_key = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
    resources = {
        node.attrib.get(xaml_key): (node.text or "").strip()
        for node in root
        if node.tag.endswith("FontFamily")
    }
    ok = True
    for style, family in legacy_families.items():
        key = "AppFontFamily" if style == "Regular" else f"AppFontFamily{style}"
        expected = (
            "avares://Animora.Desktop/Assets/Fonts/"
            f"Vazirmatn-{style}.ttf#{family}"
        )
        actual = resources.get(key)
        if actual != expected:
            print(f"  [FAIL] {key}: {actual!r}; expected {expected!r}")
            ok = False
        else:
            print(f"  [OK] {key}: exact file + ID1")

    try:
        app_root = ET.parse(APP_XAML).getroot()
    except (OSError, ET.ParseError) as exc:
        print(f"  FAIL: cannot parse {APP_XAML}: {exc}")
        return False

    selector_values: dict[str, str | None] = {}
    for node in app_root.iter():
        if not node.tag.endswith("Style") or "Selector" not in node.attrib:
            continue
        setter = next(
            (
                child
                for child in node
                if child.tag.endswith("Setter")
                and child.attrib.get("Property") == "FontFamily"
            ),
            None,
        )
        selector_values[node.attrib["Selector"]] = (
            setter.attrib.get("Value") if setter is not None else None
        )

    for style in WEIGHTS:
        if style == "Regular":
            continue
        key = f"AppFontFamily{style}"
        expected_value = f"{{StaticResource {key}}}"
        for control in ("TextBlock", "ContentPresenter"):
            selector = f"{control}[FontWeight={style}]"
            actual = selector_values.get(selector)
            if actual != expected_value:
                print(
                    f"  [FAIL] {selector}: {actual!r}; expected {expected_value!r}"
                )
                ok = False
            else:
                print(f"  [OK] {selector}: mapped to {key}")
    return ok


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
    legacy_family_users: dict[tuple[str | None, str | None], list[str]] = {}
    legacy_families: dict[str, str] = {}

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
        cmap = font.getBestCmap() or {}
        missing_probe = sorted(
            {char for char in PERSIAN_PROBE if not char.isspace() and ord(char) not in cmap}
        )

        legacy_family_users.setdefault((id1, id2), []).append(style)
        if id1:
            legacy_families[style] = id1

        problems = []
        if os2_w != expected_w:
            problems.append(f"usWeightClass={os2_w} (expected {expected_w})")
        if var_left:
            problems.append(f"variable tables left: {var_left}")
        if missing_probe:
            problems.append(f"missing Persian probe glyphs: {missing_probe}")

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

    print(f"Verifying deterministic XAML mapping in {TYPOGRAPHY_XAML} ...")
    if set(legacy_families) != set(WEIGHTS):
        print("  FAIL: cannot verify XAML mapping because font metadata is incomplete")
        ok = False
    elif not verify_xaml_mapping(legacy_families):
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
        print("OK: all static faces and deterministic per-file XAML mappings are valid.")
        return 0
    print("ERROR: font metadata or deterministic XAML mapping is broken.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
