"""Pure value converters (plan §4): dates, digits, phones, enums, money, ids.

No I/O, no globals mutated - every function is deterministic so the whole
module can be unit-smoked in isolation.
"""
from __future__ import annotations

import datetime as _dt
import hashlib
import re
from typing import Any

# ------------------------------------------------------------------ digits
_DIGIT_MAP = {**{chr(0x06F0 + i): str(i) for i in range(10)},
              **{chr(0x0660 + i): str(i) for i in range(10)}}
_DIGIT_TABLE = str.maketrans(_DIGIT_MAP)
_WS = re.compile(r"\s+")


def fold_digits(value: str | None) -> str:
    """Persian (U+06F0..) and Arabic-Indic (U+0660..) digits -> ASCII."""
    return (value or "").translate(_DIGIT_TABLE)


def clean_text(value: Any) -> str | None:
    """Trim, collapse whitespace, unify Arabic ي/ك to Persian ی/ک; empty -> None."""
    if value is None:
        return None
    s = _WS.sub(" ", str(value).replace("ي", "ی").replace("ك", "ک")).strip()
    return s or None


# ------------------------------------------------------------------ phones
def normalize_phone(value: Any) -> str | None:
    """Mirror of ``Owner.NormalizePhone``: ASCII digits only; digitless -> None."""
    if value is None:
        return None
    digits = "".join(c for c in fold_digits(str(value)) if "0" <= c <= "9")
    return digits or None


# ------------------------------------------------------------------ Jalali
# Arithmetic Persian calendar identical to .NET ``PersianCalendar``:
# leap year  <=>  (25*y + 11) % 33 < 8 ; months 1-6 = 31d, 7-11 = 30d, 12 = 29/30d.
_ANCHOR_J = 1400
_ANCHOR_G = _dt.date(2021, 3, 21)  # 1 Farvardin 1400


def _is_leap(jy: int) -> bool:
    return (25 * jy + 11) % 33 < 8


def _year_days(jy: int) -> int:
    return 366 if _is_leap(jy) else 365


def _month_days(jy: int, jm: int) -> int:
    if jm <= 6:
        return 31
    if jm <= 11:
        return 30
    return 30 if _is_leap(jy) else 29


def jalali_to_date(jy: int, jm: int, jd: int) -> _dt.date | None:
    """Jalali -> Gregorian date, ``None`` for an invalid or out-of-range date."""
    if not (1200 <= jy <= 1600 and 1 <= jm <= 12 and 1 <= jd <= _month_days(jy, jm)):
        return None
    days = 0
    if jy >= _ANCHOR_J:
        for y in range(_ANCHOR_J, jy):
            days += _year_days(y)
    else:
        for y in range(jy, _ANCHOR_J):
            days -= _year_days(y)
    days += sum(_month_days(jy, m) for m in range(1, jm)) + (jd - 1)
    return _ANCHOR_G + _dt.timedelta(days=days)


_JALALI = re.compile(r"^\s*(\d{4})\s*[/\-.]\s*(\d{1,2})\s*[/\-.]\s*(\d{1,2})(?:[ T]+(\d{1,2}):(\d{2})(?::(\d{2}))?)?")
_ISO = re.compile(r"^\s*(\d{4})-(\d{2})-(\d{2})(?:[ T](\d{2}):(\d{2})(?::(\d{2}))?)?")
_TIME = re.compile(r"(\d{1,2}):(\d{2})(?::(\d{2}))?")


def parse_time(value: Any) -> tuple[int, int, int] | None:
    """``HH:mm[:ss]`` (Persian digits allowed) -> (h, m, s); invalid -> None."""
    if value is None:
        return None
    m = _TIME.search(fold_digits(str(value)))
    if not m:
        return None
    h, mi, s = int(m.group(1)), int(m.group(2)), int(m.group(3) or 0)
    return (h, mi, s) if h < 24 and mi < 60 and s < 60 else None


def to_utc(date_value: Any, time_value: Any = None, tz_offset_minutes: int = 210) -> str | None:
    """Legacy date (Jalali ``yyyy/MM/dd`` or ISO/SQL datetime) -> ``YYYY-MM-DDTHH:MM:SSZ`` UTC.

    * Local clinic time is Iran standard time (UTC+03:30, ``tz_offset_minutes=210``);
      a date without a time maps to local noon so it never shifts day under UTC.
    * Zero / partial / invalid dates (``0000/00/00``, empty) -> ``None`` - never fabricated.
    """
    if date_value is None:
        return None
    text = fold_digits(str(date_value))
    if not text.strip():
        return None
    d: _dt.date | None = None
    hms: tuple[int, int, int] | None = None
    m = _JALALI.match(text)
    iso = _ISO.match(text)
    if m and int(m.group(1)) >= 1200 and int(m.group(1)) <= 1600:
        d = jalali_to_date(int(m.group(1)), int(m.group(2)), int(m.group(3)))
        if m.group(4):
            hms = (int(m.group(4)), int(m.group(5)), int(m.group(6) or 0))
    elif iso:
        try:
            d = _dt.date(int(iso.group(1)), int(iso.group(2)), int(iso.group(3)))
        except ValueError:
            return None
        if d.year <= 1900:  # SQL datetime 'zero' (1900-01-01) means empty
            return None
        if iso.group(4):
            hms = (int(iso.group(4)), int(iso.group(5)), int(iso.group(6) or 0))
    if d is None:
        return None
    if hms is None:
        hms = parse_time(time_value) or (12, 0, 0)
    if hms[0] > 23 or hms[1] > 59 or hms[2] > 59:
        hms = (12, 0, 0)
    local = _dt.datetime(d.year, d.month, d.day, *hms)
    utc = local - _dt.timedelta(minutes=tz_offset_minutes)
    return utc.strftime("%Y-%m-%dT%H:%M:%SZ")


# ------------------------------------------------------------------- enums
SPECIES_DOG, SPECIES_CAT, SPECIES_BIRD, SPECIES_RODENT, SPECIES_REPTILE, SPECIES_OTHER, SPECIES_RABBIT = range(7)

_SPECIES_KEYWORDS: tuple[tuple[int, tuple[str, ...]], ...] = (
    (SPECIES_RABBIT, ("خرگوش", "rabbit")),  # before rodent: rabbit is its own enum value
    (SPECIES_DOG, ("سگ", "dog", "canine", "توله")),
    (SPECIES_CAT, ("گربه", "cat", "feline")),
    (SPECIES_BIRD, ("پرنده", "طوطی", "مرغ", "کبوتر", "قناری", "عروس هلندی", "bird", "parrot")),
    (SPECIES_RODENT, ("موش", "همستر", "جونده", "خوکچه", "سنجاب", "rodent", "hamster")),
    (SPECIES_REPTILE, ("خزنده", "لاک", "مار", "مارمولک", "سوسمار", "ایگوانا", "reptile", "turtle", "snake")),
)


def map_species(name: Any) -> int:
    """Legacy animal-type name -> ``PetSpecies`` (Dog0 Cat1 Bird2 Rodent3 Reptile4 Other5 Rabbit6)."""
    s = clean_text(name)
    if not s:
        return SPECIES_OTHER
    low = s.lower()
    for species, words in _SPECIES_KEYWORDS:
        if any(w in low for w in words):
            return species
    return SPECIES_OTHER


def map_animal_sex(code: Any) -> int:
    """Legacy ``fAnimalSex`` -> ``PetGender`` (Unknown0 Male1 Female2).

    Accepts the Persian labels (نر/ماده) and the common numeric conventions
    1 = male, 2 = female; anything else is Unknown (never guessed).
    """
    return _sex(code)


def map_owner_sex(code: Any) -> int:
    """Legacy ``fSex`` -> ``OwnerGender`` (Unspecified0 Male1 Female2)."""
    return _sex(code)


def _sex(code: Any) -> int:
    if code is None or isinstance(code, bool):
        return 0
    if isinstance(code, (int, float)):
        return {1: 1, 2: 2}.get(int(code), 0)
    s = (clean_text(code) or "").lower()
    if s in ("1", "نر", "مرد", "male", "m", "آقا"):
        return 1
    if s in ("2", "ماده", "زن", "female", "f", "خانم"):
        return 2
    return 0


def map_payment_method(name: Any) -> str:
    """Payment-type label -> cash/card/transfer/cheque/other."""
    s = (clean_text(name) or "").lower()
    if any(w in s for w in ("نقد", "cash")):
        return "cash"
    if any(w in s for w in ("کارت", "pos", "پوز", "card")):
        return "card"
    if any(w in s for w in ("حواله", "واریز", "انتقال", "transfer")):
        return "transfer"
    if any(w in s for w in ("چک", "cheque", "check")):
        return "cheque"
    return "other"


# ------------------------------------------------------------------- money
def money_minor(value: Any) -> int:
    """Toman (IRT, scale 0) -> whole minor units; negatives clamp to 0 (schema: non-negative)."""
    if value is None or isinstance(value, dict):
        return 0
    try:
        n = int(round(float(value)))
    except (TypeError, ValueError):
        return 0
    return max(n, 0)


# --------------------------------------------------------------------- ids
_CROCKFORD = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"


def source_key(table: str, source_id: Any) -> str:
    """Stable human-readable source key, e.g. ``tblPatient:42``."""
    return f"{table}:{source_id}"


def stable_id(tenant_id: str, table: str, source_id: Any) -> str:
    """Deterministic 26-char Crockford id from ``hash(tenant | table | id)``.

    Same shape as an Animora ULID. The server derives ids with its own copy of
    this rule; the converter only uses it to prove determinism / for previews.
    """
    digest = hashlib.sha256(f"{tenant_id}|{table}|{source_id}".encode("utf-8")).digest()
    n = int.from_bytes(digest[:16], "big") & ((1 << 128) - 1)
    chars = []
    for _ in range(26):
        chars.append(_CROCKFORD[n & 31])
        n >>= 5
    out = "".join(reversed(chars))
    return "0" + out[1:] if out[0] > "7" else out  # keep the leading char within ULID range
