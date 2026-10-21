# Clinic.bak → Animora clinic import converter

Converts a **legacy veterinary-clinic SQL Server backup** (`Clinic.bak`) into an
**Animora clinic import bundle** (`clinic-import.animora.json`) that the vendor
admin uploads, per clinic, in the Animora admin panel. The bundle carries all of
the legacy data — owners, patients, visits, vaccinations, antiparasite records,
appointments, invoices/payments, services, products, stock and lab results —
translated into Animora's domain model, Jalali dates converted to UTC, Persian
text preserved, money kept in toman.

> The legacy database stores **one human + one animal in a single patient row**.
> The converter splits that into Animora's separate **Owner** and **Pet** records
> and de-duplicates owners who appear across many patient rows.

## Requirements

- **Python 3.10+** (standard library only — nothing to `pip install`).
- Works on Windows, macOS and Linux. The built-in reader parses the backup
  directly; you do **not** need SQL Server installed.

## Windows (one click)

1. Copy this `clinic-bak-converter` folder anywhere on the Windows machine.
2. Double-click **`run-converter.cmd`**.
3. Paste the full path to the `Clinic.bak` file when asked.
4. Press Enter to accept the default output name (or type your own path).
5. When it finishes, `clinic-import.animora.json` is written next to the script.
6. In the Animora **admin panel**, open the clinic → **Import** page and upload
   that file.

You can also pass paths directly:

```cmd
run-converter.cmd "C:\backups\Clinic.bak" "C:\out\clinic-import.animora.json"
```

## Command line (any OS)

```bash
python -m converter --bak /path/to/Clinic.bak --out clinic-import.animora.json --verbose
```

| Flag | Meaning |
|---|---|
| `--bak PATH` | input `Clinic.bak` (required) |
| `--out PATH` | output bundle path (default `clinic-import.animora.json`) |
| `--raw` | also embed a per-table raw passthrough (bigger file, full losslessness) |
| `--verbose` | print per-table row counts and warnings |

## Output & privacy

The bundle contains **personal data** (owner names, phones, national ids). Keep
it local; do **not** commit it to source control or send it over untrusted
channels. Upload it only through the authenticated admin panel.

## Status

Scaffold in place (CLI + Windows launcher). The conversion pipeline
(reader → extractors → mapping → bundle) is implemented across the remaining
tasks; see `../../Plan/` while the plan file still exists, and `FORMAT.md` for
the bundle contract.
