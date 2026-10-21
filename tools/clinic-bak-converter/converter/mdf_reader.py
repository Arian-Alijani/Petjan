"""Read-only SQL Server MDF page reader embedded in an MTF ``.bak`` (stdlib only).

Generalised from the proven ``scripts/extract-clinic-bak.py`` reader:

* the MDF stream sits inside the MTF container at a fixed base offset
  (``7680`` for the known backups; other 512-aligned bases are auto-detected),
* data pages are 8192 bytes; only live primary data records are decoded,
* the system catalog (``sysschobjs`` -> ``syscolpars`` -> ``sysrowsets`` ->
  ``sysallocunits`` -> ``sysrscols``) binds columns to rows *by name*.

Not a RESTORE replacement. Compressed/encrypted backups, LOB pages and
row-overflow values are detected and reported through ``warnings`` - they are
never guessed (the affected value becomes ``None``).
"""
from __future__ import annotations

import collections
import datetime as _dt
import hashlib
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

PAGE = 8192
DEFAULT_BASE = 7680
HEADER = 96
USER_TABLE_PREFIX = "tbl"

# System base-table allocation ids (object ids of the hard-wired catalog tables).
_SYSSCHOBJS, _SYSCOLPARS, _SYSROWSETS, _SYSALLOCUNITS, _SYSRSCOLS = 34, 41, 5, 7, 3

# SQL Server type ids (sys.types.system_type_id)
T_TINYINT, T_SMALLINT, T_INT, T_BIGINT = 48, 52, 56, 127
T_BIT, T_FLOAT, T_REAL = 104, 62, 59
T_DATETIME, T_SMALLDATETIME, T_DATE = 61, 58, 40
T_MONEY, T_SMALLMONEY = 60, 122
T_DECIMAL, T_NUMERIC = 106, 108
T_VARCHAR, T_CHAR, T_NVARCHAR, T_NCHAR = 167, 175, 231, 239
T_TEXT, T_NTEXT, T_IMAGE = 35, 99, 34
T_VARBINARY, T_BINARY, T_GUID = 165, 173, 36
_LOB_TYPES = (T_TEXT, T_NTEXT, T_IMAGE)
_EPOCH_1900 = _dt.datetime(1900, 1, 1)


class MdfError(ValueError):
    """The backup is not in the (supported) shape this reader understands."""


def _u16(b: bytes, p: int = 0) -> int:
    return struct.unpack_from("<H", b, p)[0]


def _u32(b: bytes, p: int = 0) -> int:
    return struct.unpack_from("<I", b, p)[0]


@dataclass
class Column:
    name: str
    ordinal: int
    type: int
    length: int
    precision: int = 0
    scale: int = 0
    physical_ordinal: int = 0
    offset: int = 0
    bit: int = 0


@dataclass
class Table:
    name: str
    object_id: int
    columns: list[Column] = field(default_factory=list)
    allocation: int | None = None
    expected_rows: int = 0
    extracted_rows: int = 0


class MdfReader:
    """Parse an in-memory ``.bak`` and expose its user tables as dict rows."""

    def __init__(self, data: bytes, base: int | None = None) -> None:
        self.data = data
        self.sha256 = hashlib.sha256(data).hexdigest()
        self.warnings: list[str] = []
        self.base = self._detect_base() if base is None else base
        self.page_windows = 0
        self.data_pages = 0
        self._groups = self._read_pages()
        if not self._groups:
            raise MdfError("no SQL Server data pages found (compressed/encrypted or unsupported .bak)")
        self.tables: dict[str, Table] = self._read_catalog()
        if not self.tables:
            raise MdfError("no user tables found in the system catalog")
        self._cache: dict[str, list[dict[str, Any]]] = {}

    # ------------------------------------------------------------ construction
    @classmethod
    def from_path(cls, path: str | Path) -> "MdfReader":
        return cls(Path(path).read_bytes())

    def _is_live_page(self, page: bytes) -> bool:
        return page[:2] == b"\x01\x01" and _u16(page, 36) == 1

    def _detect_base(self) -> int:
        """Prefer the proven base; otherwise probe 512-aligned offsets."""
        def score(base: int) -> int:
            hits = 0
            for off in range(base, min(len(self.data) - PAGE + 1, base + PAGE * 400), PAGE):
                if self._is_live_page(self.data[off:off + PAGE]):
                    hits += 1
            return hits

        if score(DEFAULT_BASE):
            return DEFAULT_BASE
        best, best_score = DEFAULT_BASE, 0
        for base in range(0, min(len(self.data), 1 << 17), 512):
            s = score(base)
            if s > best_score:
                best, best_score = base, s
        if best_score:
            self.warnings.append(f"MDF base offset auto-detected at {best} (expected {DEFAULT_BASE})")
        return best

    # ----------------------------------------------------------------- pages
    def _read_pages(self) -> dict[int, list[tuple[bytes, list[Any], int, bytes]]]:
        groups: dict[int, list] = collections.defaultdict(list)
        data = self.data
        for off in range(self.base, len(data) - PAGE + 1, PAGE):
            self.page_windows += 1
            page = data[off:off + PAGE]
            if not self._is_live_page(page):
                continue
            slots = _u16(page, 22)
            if slots == 0:
                continue
            if not 0 < slots < 1000:
                raise MdfError(f"invalid page slot count at offset {off}")
            self.data_pages += 1
            end = PAGE - 2 * slots
            for i in range(slots):
                start = _u16(page, PAGE - 2 * (i + 1))
                # Logical slot order != physical order: records carry their own lengths.
                if not HEADER <= start < end:
                    raise MdfError(f"invalid record bounds at offset {off}")
                rec = page[start:end]
                # Primary data records only; ghost/forwarded rows are not live data.
                if rec[0] & 14 or not rec[0] & 16:
                    continue
                fixed_end = _u16(rec, 2)
                if not 4 <= fixed_end <= len(rec) - 2:
                    raise MdfError(f"unsupported record header at offset {off}")
                count = _u16(rec, fixed_end)
                nulls = rec[fixed_end + 2:fixed_end + 2 + (count + 7) // 8]
                pos = fixed_end + 2 + len(nulls)
                variables: list[Any] = []
                if rec[0] & 32:
                    nvar = _u16(rec, pos)
                    pos += 2
                    ends = [_u16(rec, pos + 2 * k) for k in range(nvar)]
                    pos += 2 * nvar
                    for encoded in ends:
                        stop = encoded & 32767
                        if not pos <= stop <= len(rec):
                            raise MdfError(f"invalid variable-column bounds at offset {off}")
                        raw = rec[pos:stop]
                        # bit 15 = complex column (LOB / row-overflow pointer)
                        variables.append({"complex": raw.hex()} if encoded & 32768 else raw)
                        pos = stop
                groups[_u32(page, 24)].append((rec[4:fixed_end], variables, count, nulls))
        return groups

    # --------------------------------------------------------------- catalog
    def _read_catalog(self) -> dict[str, Table]:
        g = self._groups
        by_id: dict[int, Table] = {}
        for fixed, values, _, _ in g.get(_SYSSCHOBJS, []):
            if values and isinstance(values[0], bytes):
                name = values[0].decode("utf-16-le")
                if name.startswith(USER_TABLE_PREFIX):
                    by_id[_u32(fixed)] = Table(name=name, object_id=_u32(fixed))
        for fixed, values, _, _ in g.get(_SYSCOLPARS, []):
            table = by_id.get(_u32(fixed))
            if table and values and isinstance(values[0], bytes) and _u16(fixed, 4) == 0:
                table.columns.append(Column(
                    name=values[0].decode("utf-16-le"), ordinal=_u32(fixed, 6),
                    type=fixed[10], length=_u16(fixed, 15), precision=fixed[17], scale=fixed[18]))
        allocations: dict[bytes, int] = {}
        for fixed, _, _, _ in g.get(_SYSALLOCUNITS, []):
            if fixed[8] == 1:  # IN_ROW_DATA
                allocations[bytes(fixed[9:17])] = int.from_bytes(fixed[:8], "little") >> 16 & 0xFFFFFFFF
        layouts: dict[bytes, dict[int, dict[str, int]]] = collections.defaultdict(dict)
        for fixed, _, _, _ in g.get(_SYSRSCOLS, []):
            layouts[bytes(fixed[:8])][_u32(fixed, 8)] = {
                "physical_ordinal": _u32(fixed, 12),
                "offset": struct.unpack_from("<h", fixed, 40)[0],
                "bit": fixed[48]}
        for fixed, _, _, _ in g.get(_SYSROWSETS, []):
            table = by_id.get(_u32(fixed, 9))
            if table is not None and _u32(fixed, 13) in (0, 1):
                rid = bytes(fixed[:8])
                table.allocation = allocations.get(rid)
                table.expected_rows = int.from_bytes(fixed[27:35], "little")
                for col in table.columns:
                    layout = layouts[rid].get(col.ordinal)
                    if layout:
                        col.physical_ordinal = layout["physical_ordinal"]
                        col.offset = layout["offset"]
                        col.bit = layout["bit"]
        out: dict[str, Table] = {}
        for table in by_id.values():
            table.columns.sort(key=lambda c: c.ordinal)
            if table.allocation is None or not table.columns or any(c.physical_ordinal == 0 for c in table.columns):
                self.warnings.append(f"table {table.name}: incomplete catalog metadata, skipped")
                continue
            out[table.name] = table
        return out

    # ---------------------------------------------------------------- decode
    def _decode_value(self, col: Column, raw: bytes) -> Any:
        t = col.type
        if t in (T_NVARCHAR, T_NCHAR, T_NTEXT):
            return raw.decode("utf-16-le", errors="replace").rstrip()
        if t in (T_VARCHAR, T_CHAR, T_TEXT):
            return raw.decode("cp1256", errors="replace").rstrip()
        if t in (T_TINYINT, T_SMALLINT, T_INT, T_BIGINT):
            return int.from_bytes(raw, "little", signed=t != T_TINYINT)
        if t == T_BIT:
            return bool(raw[0])
        if t == T_FLOAT:
            return struct.unpack("<d", raw)[0]
        if t == T_REAL:
            return struct.unpack("<f", raw)[0]
        if t == T_DATETIME:
            days, ticks = struct.unpack("<iI", raw)
            return (_EPOCH_1900 + _dt.timedelta(days=days, milliseconds=round(ticks * 10 / 3))).isoformat(sep=" ")
        if t == T_SMALLDATETIME:
            days, minutes = struct.unpack("<HH", raw)
            return (_EPOCH_1900 + _dt.timedelta(days=days, minutes=minutes)).isoformat(sep=" ")
        if t == T_DATE:
            return (_dt.date(1, 1, 1) + _dt.timedelta(days=int.from_bytes(raw[:3], "little"))).isoformat()
        if t in (T_MONEY, T_SMALLMONEY):
            return int.from_bytes(raw, "little", signed=True) / 10000
        if t in (T_DECIMAL, T_NUMERIC):
            magnitude = int.from_bytes(raw[1:], "little")
            value = magnitude if raw[0] == 1 else -magnitude
            return value / (10 ** col.scale) if col.scale else value
        if t == T_GUID:
            import uuid
            return str(uuid.UUID(bytes_le=raw))
        # binary / image / unknown: keep as hex so nothing is silently lost
        return {"sqlType": t, "hex": raw.hex()}

    def _decode_row(self, table: Table, row: tuple[bytes, list[Any], int, bytes]) -> dict[str, Any]:
        fixed, variables, count, nulls = row
        if count > 1024:
            raise MdfError(f"{table.name}: schema/row column count mismatch")
        out: dict[str, Any] = {}
        for col in table.columns:
            index = col.physical_ordinal - 1
            # ALTER TABLE ADD can append nullable columns without rewriting old rows.
            if index >= count or nulls[index // 8] & (1 << (index % 8)):
                out[col.name] = None
                continue
            if col.offset < 0:
                var = -col.offset - 1
                raw = variables[var] if var < len(variables) else b""
                if isinstance(raw, dict):
                    self.warnings.append(f"{table.name}.{col.name}: LOB/row-overflow pointer skipped (value set to null)")
                    out[col.name] = None
                    continue
            elif col.type == T_BIT:
                raw = bytes([(fixed[col.offset - 4] >> col.bit) & 1])
            else:
                raw = fixed[col.offset - 4:col.offset - 4 + col.length]
                if len(raw) != col.length:
                    raise MdfError(f"{table.name}.{col.name}: fixed column bounds mismatch")
            out[col.name] = self._decode_value(col, raw)
        return out

    # ------------------------------------------------------------------- API
    def table_names(self) -> list[str]:
        return sorted(self.tables)

    def has_table(self, name: str) -> bool:
        return name in self.tables

    def columns(self, name: str) -> list[str]:
        return [c.name for c in self.tables[name].columns]

    def rows(self, name: str) -> list[dict[str, Any]]:
        """All live rows of ``name`` as ``{column: value}`` dicts (empty if the table is absent)."""
        table = self.tables.get(name)
        if table is None:
            return []
        cached = self._cache.get(name)
        if cached is None:
            cached = [self._decode_row(table, r) for r in self._groups.get(table.allocation, [])]
            table.extracted_rows = len(cached)
            if len(cached) != table.expected_rows:
                self.warnings.append(
                    f"{name}: extracted {len(cached)} rows vs {table.expected_rows} in catalog "
                    "(deleted slots / stale metadata)")
            self._cache[name] = cached
        return cached

    def row_counts(self) -> dict[str, int]:
        return {name: len(self.rows(name)) for name in self.table_names()}
