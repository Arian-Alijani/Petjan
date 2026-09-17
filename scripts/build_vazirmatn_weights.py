#!/usr/bin/env python3
"""
Normalize the bundled static Vazirmatn weight files into ONE clean, canonical
multi-weight family so Avalonia's font matcher resolves every FontWeight to the
correct outlines instead of rendering heavy weights (Bold/ExtraBold/Black) as
solid black rectangles (.notdef "tofu").

--------------------------------------------------------------------------------
Bug history / root cause
--------------------------------------------------------------------------------
1. Upstream Vazirmatn ships a single VARIABLE font (wght 100..900). Avalonia
   cannot weight-resolve a variable font, so a first attempt to use it rendered
   every non-Regular label as tofu.

2. The first fix instanced discrete static weights, but split the LEGACY family
   name (name ID 1) per weight ("Vazirmatn SemiBold", "Vazirmatn ExtraBold", ...)
   while keeping only the TYPOGRAPHIC family (ID 16) equal to "Vazirmatn". It
   also left the variable-font leftovers behind (a STAT table still advertising
   the Weight axis, plus fvar named-instance name records 256+ and a stale
   Unique-ID / name ID 3 that still said "...Vazirmatn-Regular").

   Avalonia 12.x builds an EmbeddedFontCollection keyed by the family name that
   the platform typeface reports. When the six files disagree on the family name
   (some say "Vazirmatn", some say "Vazirmatn ExtraBold") AND still carry a STAT
   Weight axis + named-instance records, the matcher cannot reliably collapse
   them into one "Vazirmatn" family with six weights. Requesting the heaviest
   weights (ExtraBold 800 / Black 900) then fails to bind to real outlines and
   falls back to .notdef -> solid black rectangles.

--------------------------------------------------------------------------------
The canonical fix (this script)
--------------------------------------------------------------------------------
Rewrite every static file so they form ONE unambiguous static family:
  * name ID 1 (legacy family)  = "Vazirmatn"   for ALL six weights
  * name ID 2 (legacy subfam.) = RIBBI style   ("Regular" or "Bold")
  * name ID 16 (typo family)   = "Vazirmatn"   for ALL six weights
  * name ID 17 (typo subfamily)= real style     (Regular/Medium/.../Black)
  * name ID 4  (full)          = "Vazirmatn <Style>"
  * name ID 6  (PostScript)    = "Vazirmatn-<Style>"
  * name ID 3  (unique id)     = "Vazirmatn-<Style>"  (was stale "...Regular")
  * OS/2.usWeightClass         = 400/500/600/700/800/900
  * OS/2.fsSelection / head.macStyle bold+regular bits set correctly
  * Remove leftover variable tables (STAT/fvar/gvar/avar/HVAR/VVAR/MVAR) and the
    fvar named-instance / axis name records (nameID >= 256) so nothing advertises
    a variable axis anymore.

Because Regular(400) and Bold(700) use the RIBBI subfamily ("Regular"/"Bold")
while Medium/SemiBold/ExtraBold/Black also keep legacy family "Vazirmatn" (with
their real usWeightClass), the matcher sees a single "Vazirmatn" family with six
distinct weights and binds each requested FontWeight to real glyph outlines.

Idempotent: run it against the already-bundled static TTFs (the variable original
is no longer in the repo). It reads each Vazirmatn-<Style>.ttf, normalizes it, and
writes it back in place.
"""
from pathlib import Path
from fontTools.ttLib import TTFont

FONTS_DIR = Path("src/Animora.Desktop/Assets/Fonts")
FAMILY = "Vazirmatn"

# style -> usWeightClass
WEIGHTS = {
    "Regular": 400,
    "Medium": 500,
    "SemiBold": 600,
    "Bold": 700,
    "ExtraBold": 800,
    "Black": 900,
}

# Variable-font tables that must not survive in a static instance.
VAR_TABLES = ("STAT", "fvar", "gvar", "avar", "HVAR", "VVAR", "MVAR", "cvar")


def set_name(name_table, name_id, value):
    """Set a name record on Windows (3,1,0x409) and Mac (1,0,0) platforms."""
    name_table.setName(value, name_id, 3, 1, 0x409)
    name_table.setName(value, name_id, 1, 0, 0)


def normalize(style: str, weight: int):
    path = FONTS_DIR / f"Vazirmatn-{style}.ttf"
    font = TTFont(str(path))

    is_bold = weight >= 700  # RIBBI "bold" bit for 700+

    # --- OS/2 weight + selection bits ---
    os2 = font["OS/2"]
    os2.usWeightClass = weight
    fs = os2.fsSelection
    fs &= ~(1 << 0)              # clear ITALIC
    if is_bold:
        fs |= (1 << 5)          # set BOLD
        fs &= ~(1 << 6)         # clear REGULAR
    else:
        fs &= ~(1 << 5)         # clear BOLD
        fs |= (1 << 6)          # set REGULAR
    os2.fsSelection = fs

    # --- head.macStyle bold bit ---
    head = font["head"]
    if is_bold:
        head.macStyle |= 0x01
    else:
        head.macStyle &= ~0x01

    # --- names: one canonical "Vazirmatn" family for every weight ---
    name = font["name"]

    # RIBBI legacy subfamily: only Regular/Bold are valid RIBBI styles; every
    # non-RIBBI weight still lives under the SAME legacy family "Vazirmatn" and is
    # disambiguated purely by usWeightClass. This keeps all six in one family so
    # the matcher can weight-resolve them.
    legacy_sub = "Bold" if is_bold else "Regular"
    full = f"{FAMILY} {style}"
    ps = f"{FAMILY}-{style}"

    set_name(name, 1, FAMILY)          # Legacy family  (SAME for all weights)
    set_name(name, 2, legacy_sub)      # Legacy subfamily (RIBBI)
    set_name(name, 3, ps)              # Unique ID (was stale "...Regular")
    set_name(name, 4, full)            # Full name
    set_name(name, 6, ps)              # PostScript name
    set_name(name, 16, FAMILY)         # Typographic family (SAME for all weights)
    set_name(name, 17, style)          # Typographic subfamily (real style)

    # Drop fvar named-instance / variation axis name records (IDs 256+) that the
    # variable original left behind; they make matchers think this is still a
    # variable/named-instance face.
    name.names = [r for r in name.names if r.nameID < 256]

    # --- drop leftover variable-font tables ---
    removed = []
    for tag in VAR_TABLES:
        if tag in font:
            del font[tag]
            removed.append(tag)

    font.save(str(path))
    print(
        f"  -> {path.name:24s} usWeightClass={weight} bold={is_bold} "
        f"removed={removed or '-'}"
    )


def main():
    print(f"Normalizing static Vazirmatn weights in {FONTS_DIR} ...")
    for style, weight in WEIGHTS.items():
        normalize(style, weight)
    print("Done. All six files now form one canonical 'Vazirmatn' family.")


if __name__ == "__main__":
    main()
