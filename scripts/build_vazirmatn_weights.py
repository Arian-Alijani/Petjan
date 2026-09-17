#!/usr/bin/env python3
"""
Build static Vazirmatn weight instances from the variable Vazirmatn.ttf.

Root cause of the "black rectangle instead of text" bug:
  The bundled Vazirmatn.ttf is a VARIABLE font (wght 100..900) exposing only
  the default Regular (400) named instance. Avalonia does not resolve arbitrary
  variable-font weights, so any FontWeight other than Regular (Bold/SemiBold/
  ExtraBold/Medium) had no matching glyph outlines and rendered as .notdef
  (solid filled rectangles).

Fix:
  Instantiate discrete static weights (400/500/600/700/800/900) that all share
  the SAME family name ("Vazirmatn") but carry the correct usWeightClass +
  subfamily naming, so Avalonia's font matcher can pick the right file per
  requested FontWeight.
"""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

SRC = Path("src/Animora.Desktop/Assets/Fonts/Vazirmatn.ttf")
OUT_DIR = SRC.parent

FAMILY = "Vazirmatn"

# weight value -> (subfamily/style name, macStyle bold bit, fsSelection tweaks)
WEIGHTS = {
    400: "Regular",
    500: "Medium",
    600: "SemiBold",
    700: "Bold",
    800: "ExtraBold",
    900: "Black",
}


def set_name(name_table, name_id, value):
    """Set a name record for both Windows (3,1,0x409) and Mac (1,0,0) platforms."""
    name_table.setName(value, name_id, 3, 1, 0x409)
    name_table.setName(value, name_id, 1, 0, 0)


def build_weight(weight: int, style: str):
    font = TTFont(str(SRC))
    instantiateVariableFont(font, {"wght": weight}, inplace=True)

    is_bold = weight >= 700  # treat 700+ as the "bold" style bit

    # --- OS/2 ---
    os2 = font["OS/2"]
    os2.usWeightClass = weight
    # fsSelection: bit0=ITALIC, bit5=BOLD, bit6=REGULAR (0x40)
    fs = os2.fsSelection
    fs &= ~(1 << 0)          # clear italic
    if is_bold:
        fs |= (1 << 5)       # set bold
        fs &= ~(1 << 6)      # clear regular
    else:
        fs &= ~(1 << 5)      # clear bold
        fs |= (1 << 6)       # set regular
    os2.fsSelection = fs

    # --- head.macStyle ---
    head = font["head"]
    if is_bold:
        head.macStyle |= 0x01
    else:
        head.macStyle &= ~0x01

    # --- naming ---
    #   Use the modern typographic family (IDs 16/17) so ALL weights collapse
    #   into a single "Vazirmatn" family, while legacy IDs 1/2 keep the RIBBI
    #   grouping that older matchers expect.
    name = font["name"]
    set_name(name, 16, FAMILY)          # Typographic Family
    set_name(name, 17, style)           # Typographic Subfamily

    if style in ("Regular", "Bold"):
        legacy_family = FAMILY
        legacy_sub = style
    else:
        # e.g. "Vazirmatn SemiBold" / "Regular" so each non-RIBBI weight is its
        # own legacy family and never gets faux-bolded.
        legacy_family = f"{FAMILY} {style}"
        legacy_sub = "Regular"
    set_name(name, 1, legacy_family)    # Legacy Family
    set_name(name, 2, legacy_sub)       # Legacy Subfamily

    full = f"{FAMILY} {style}"
    ps = f"{FAMILY}-{style}"
    set_name(name, 4, full)             # Full name
    set_name(name, 6, ps)               # PostScript name

    out = OUT_DIR / f"Vazirmatn-{style}.ttf"
    font.save(str(out))
    print(f"  -> {out.name:28s} wght={weight} usWeightClass={os2.usWeightClass} bold={is_bold}")


def main():
    print(f"Instancing {SRC} ...")
    for w, style in WEIGHTS.items():
        build_weight(w, style)
    print("Done.")


if __name__ == "__main__":
    main()
