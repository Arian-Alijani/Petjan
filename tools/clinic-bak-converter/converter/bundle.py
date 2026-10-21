"""Bundle writer + stdlib JSON-Schema validation + human summary (plan T7)."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

SCHEMA_PATH = Path(__file__).resolve().parent.parent / "schema" / "clinic-import.schema.json"

_TYPES = {
    "object": lambda v: isinstance(v, dict),
    "array": lambda v: isinstance(v, list),
    "string": lambda v: isinstance(v, str),
    "boolean": lambda v: isinstance(v, bool),
    "integer": lambda v: isinstance(v, int) and not isinstance(v, bool),
    "number": lambda v: isinstance(v, (int, float)) and not isinstance(v, bool),
    "null": lambda v: v is None,
}
MAX_ERRORS = 50


def load_schema(path: Path | None = None) -> dict[str, Any]:
    return json.loads((path or SCHEMA_PATH).read_text(encoding="utf-8"))


def validate(instance: Any, schema: dict[str, Any]) -> list[str]:
    """Validate ``instance`` against the subset of JSON Schema 2020-12 the bundle schema uses.

    Supported: type (string|list), const, enum, properties, required,
    additionalProperties (bool), items, minimum, maximum, minLength, local ``$ref``.
    Returns up to ``MAX_ERRORS`` human-readable errors (empty list == valid).
    """
    errors: list[str] = []
    defs = schema.get("$defs", {})

    def walk(value: Any, node: dict[str, Any], path: str) -> None:
        if len(errors) >= MAX_ERRORS:
            return
        ref = node.get("$ref")
        if ref:
            node = defs[ref.rsplit("/", 1)[-1]]
        if "const" in node and value != node["const"]:
            errors.append(f"{path}: expected {node['const']!r}")
            return
        if "enum" in node and value not in node["enum"]:
            errors.append(f"{path}: {value!r} not in {node['enum']}")
            return
        types = node.get("type")
        if types is not None:
            allowed = types if isinstance(types, list) else [types]
            if not any(_TYPES[t](value) for t in allowed):
                errors.append(f"{path}: expected {'|'.join(allowed)}, got {type(value).__name__}")
                return
        if isinstance(value, bool):
            return
        if isinstance(value, (int, float)):
            if "minimum" in node and value < node["minimum"]:
                errors.append(f"{path}: {value} < {node['minimum']}")
            if "maximum" in node and value > node["maximum"]:
                errors.append(f"{path}: {value} > {node['maximum']}")
        elif isinstance(value, str):
            if len(value) < node.get("minLength", 0):
                errors.append(f"{path}: shorter than {node['minLength']}")
        elif isinstance(value, dict):
            for req in node.get("required", []):
                if req not in value:
                    errors.append(f"{path}: missing required '{req}'")
            props = node.get("properties", {})
            for key, sub in value.items():
                if key in props:
                    walk(sub, props[key], f"{path}.{key}")
                elif node.get("additionalProperties") is False:
                    errors.append(f"{path}: unexpected property '{key}'")
        elif isinstance(value, list) and "items" in node:
            for i, item in enumerate(value):
                walk(item, node["items"], f"{path}[{i}]")

    walk(instance, schema, "$")
    return errors


def referential_errors(bundle: dict[str, Any]) -> list[str]:
    """Cross-entity checks the JSON Schema cannot express (FK resolution, uniqueness)."""
    errors: list[str] = []
    pet_ids: set[str] = set()
    for owner in bundle.get("owners", []):
        for pet in owner.get("pets", []):
            if pet["sourceId"] in pet_ids:
                errors.append(f"duplicate pet sourceId {pet['sourceId']}")
            pet_ids.add(pet["sourceId"])

    def ids(key: str) -> set[str]:
        return {e["sourceId"] for e in bundle.get(key, [])}

    refs = {
        "visits": {"petSourceId": pet_ids, "doctorSourceId": ids("staff")},
        "vaccinations": {"petSourceId": pet_ids, "vaccineTypeSourceId": ids("vaccineTypes")},
        "antiparasites": {"petSourceId": pet_ids},
        "labResults": {"petSourceId": pet_ids},
        "appointments": {"petSourceId": pet_ids, "doctorSourceId": ids("staff")},
        "invoices": {"petSourceId": pet_ids, "insurerSourceId": ids("insurers"),
                     "paymentTypeSourceId": ids("paymentTypes")},
        "services": {"staffSourceId": ids("staff")},
        "products": {"categorySourceId": ids("categories")},
    }
    for key, fks in refs.items():
        seen: set[tuple[str, str]] = set()
        for entity in bundle.get(key, []):
            ident = (entity.get("sourceTable", ""), entity["sourceId"])
            if ident in seen:
                errors.append(f"{key}: duplicate sourceId {entity['sourceId']}")
            seen.add(ident)
            for fk, known in fks.items():
                value = entity.get(fk)
                if value is not None and value not in known:
                    errors.append(f"{key}[{entity['sourceId']}].{fk}: unresolved '{value}'")
    products, services = ids("products"), ids("services")
    for inv in bundle.get("invoices", []):
        for line in inv.get("lines", []):
            for fk, known in (("productSourceId", products), ("serviceSourceId", services)):
                if line.get(fk) is not None and line[fk] not in known:
                    errors.append(f"invoices[{inv['sourceId']}].lines[{line['sourceId']}].{fk}: unresolved")
    for receipt in bundle.get("stockReceipts", []):
        for line in receipt.get("lines", []):
            if line["productSourceId"] not in products:
                errors.append(f"stockReceipts[{receipt['sourceId']}].lines[{line['sourceId']}]: unresolved product")
    return errors[:MAX_ERRORS]


def write_bundle(bundle: dict[str, Any], out_path: Path) -> None:
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(bundle, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")


def summarize(bundle: dict[str, Any], verbose: bool = False) -> str:
    owners = bundle.get("owners", [])
    lines = [
        f"Bundle format v{bundle['formatVersion']} - converter {bundle['converterVersion']}",
        f"  owners:        {len(owners)}",
        f"  pets:          {sum(len(o['pets']) for o in owners)}",
    ]
    for key in ("visits", "vaccinations", "antiparasites", "labResults", "appointments", "invoices",
                "stockReceipts", "services", "products", "staff", "insurers", "holidays"):
        lines.append(f"  {key + ':':<14} {len(bundle.get(key, []))}")
    warnings = bundle.get("warnings", [])
    lines.append(f"  warnings:      {len(warnings)}")
    shown = warnings if verbose else warnings[:10]
    lines.extend(f"    - {w}" for w in shown)
    if len(shown) < len(warnings):
        lines.append(f"    ... {len(warnings) - len(shown)} more (use --verbose)")
    if verbose:
        lines.append("  source row counts:")
        lines.extend(f"    {k}: {v}" for k, v in bundle.get("sourceRowCounts", {}).items())
    return "\n".join(lines)
