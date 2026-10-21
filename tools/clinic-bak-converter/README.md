# Clinic.bak → Animora Clinic Import Converter (`tools/clinic-bak-converter`)

Standalone, zero-dependency converter that transforms a legacy veterinary-clinic SQL Server backup (`Clinic.bak`, source **A**) into an **Animora format-v2 clinic import bundle** (`clinic-import.animora.json`, file **B**) for per-clinic upload in the Animora vendor admin panel.

- **Pure Python 3.10+ standard library** — no `pip install`, no SQL Server / LocalDB / Docker required.
- **Deterministic & idempotent** — every source row keeps its `(sourceTable, sourceId)` key; the Animora server deterministically derives 26-char Crockford ULIDs from `SHA-256(tenantId | table | sourceId)`, so re-importing the same bundle into a clinic updates/preserves existing records without duplicating rows or breaking foreign keys.
- **Lossless** — legacy columns with no direct Animora property are preserved under `legacy` on the nearest entity, and tables with no Animora aggregate (`tblWare`, `tblConsumerStuff`, `tblBloodTest`, `tblDays`, `tblStaffDaysStatus`) are retained under `archive` (plus optional full table dump via `--raw`).

---

## 1. End-to-End Windows Workflow (One-Click `.cmd` → Admin Panel)

### Step A — Convert `Clinic.bak` on Windows
1. Copy the `tools/clinic-bak-converter` folder to the Windows machine (ensure **Python 3.10+** is installed and available via `py` or `python`).
2. Double-click **`run-converter.cmd`**.
3. When prompted:
   - Paste or type the full path to the customer's `.bak` file (for example `C:\Backups\Clinic.bak`) and press **Enter**.
   - Press **Enter** to accept the default output file name (`clinic-import.animora.json`), or enter a custom output path.
4. The converter parses the embedded MDF pages directly (`BASE=7680`, `8192`-byte pages), extracts all legacy tables, maps them to the format-v2 bundle, validates the bundle against [`schema/clinic-import.schema.json`](schema/clinic-import.schema.json) and referential-integrity rules, and prints a summary of extracted entities and any warnings.

You can also invoke the launcher non-interactively from `cmd.exe` or PowerShell:

```cmd
run-converter.cmd "C:\Backups\Clinic.bak" "C:\Out\clinic-import.animora.json"
```

### Step B — Import into a Clinic in the Animora Admin Panel
1. Sign in to **`Animora.Web.Admin`** as a vendor operator.
2. Navigate to **Tenants (کلینیک‌ها)** → select the target clinic → click **بارگذاری بستهٔ اطلاعات قدیم (Import Legacy Bundle)** (`/admin/tenants/{tenantId}/import`).
3. **Dry-run validation (پیش‌نمایش و اعتبارسنجی):**
   - Select `clinic-import.animora.json` and click **اعتبارسنجی بسته (Validate)**.
   - Review the entity counts (owners, pets, visits, vaccinations, antiparasites, lab results, appointments, invoices, stock receipts, products, services, categories, insurers), `sourceFingerprint`, and any converter warnings.
4. **Commit import (ورود نهایی به کلینیک):**
   - Re-select the bundle file, check the confirmation box, and click **اجرای ورود اطلاعات به کلینیک (Import)**.
   - The server ingests the bundle inside a dedicated tenant-bound transaction (`TenantBoundContext`), stamps all rows with the clinic's `TenantId`, records operator and security audit entries, and makes the data available to the clinic's desktop clients via standard sync.

---

## 2. Command-Line Usage (Windows / macOS / Linux)

```bash
python3 -m converter --bak /path/to/Clinic.bak --out clinic-import.animora.json --verbose
```

| Option | Description |
|---|---|
| `--bak PATH` | Path to the input `Clinic.bak` backup file (**required**) |
| `--out PATH` | Output bundle path (default: `clinic-import.animora.json`) |
| `--raw` | Include a top-level `raw` object with all extracted rows per legacy table |
| `--verbose` | Print per-table row counts, entity summary, and warnings to stderr |

---

## 3. Source (A) → Target (B) Mapping Summary

Full field-level contract is documented in [`FORMAT.md`](FORMAT.md) and enforced by [`schema/clinic-import.schema.json`](schema/clinic-import.schema.json).

| Legacy Source (`Clinic.bak`) | Bundle Section (`formatVersion: 2`) | Animora Target Aggregate / Domain Rule |
|---|---|---|
| `tblPatient` + `tblEnglishInfo` | `owners[]` + nested `pets[]` | **Split & de-duplicate:** each patient row becomes one `Pet`; human owners are de-duplicated by valid 10-digit `fNationalCode`, else normalized `(fullName, primaryPhone)`, else per-row fallback (`owner:<lowest fId>`). |
| `tblAnimalType` | `species[]` + `pets[].species` | Mapped to `PetSpecies` (`Dog=0, Cat=1, Bird=2, Rodent=3, Reptile=4, Other=5, Rabbit=6`). Original Persian species name is preserved in `speciesName` / `breed` so nothing is lost when folded to `Other`. |
| `fAnimalSex` / `fSex` | `pets[].gender` / `owners[].gender` | `نر` → `Male (1)`, `ماده` → `Female (2)`, otherwise `Unknown/Unspecified (0)`. |
| `fCodeMicrochip` + `fDateKasht` | `pets[].microchipCode`, `microchipImplantUtc` | Domain requires implant date when setting microchip (`Pet.SetMicrochip`). If implant date is absent, code is preserved in `pets[].legacy.microchipCode`. |
| `tblInsurances` | `insurers[]` | `Insurer` (`sales.insurer.create`). |
| `tblPaymentType` | `paymentTypes[]` | Inferred `method`: `cash`, `card`, `transfer`, `cheque`, `other`. |
| `tblStuffGroup` | `categories[]` | `ProductCategory` (`inventory.category.create`). |
| `tblVaccinationType` | `vaccineTypes[]` | `VaccineCatalogItem` (`vaccination.catalog.add`). |
| `tblStaff` (+ `tblDesignation`, `tblSpecialty`) | `staff[]` | Staff directory reference; doctor names preserved on imported visits/notes. |
| `tblServices` | `services[]` | Service catalog (`Product` with `ProductKind.Service`, `trackStock: false`). |
| `tblStuff` | `products[]` | Stock-tracked `Product` + default `ProductVariant` (`IRT` scale 0). |
| `tblPatientRecord` + `tblPaientRecordDetail` (`fType`: 0=Symptom, 2=Treatment, 3=Complaint) | `visits[]` | Completed `Visit` (`Diagnosis`, `TreatmentPlan`, `DoctorNotes`) + `MedicalHistoryEntry` (`MedicalHistoryKind.Visit`). |
| `tblVaccination` | `vaccinations[]` | `VaccinationRecord` (`fDate` → `administeredUtc`, `fNextDate` → `nextDueUtc`) + `MedicalHistoryEntry` (`Vaccination`). |
| `tblAntiParasite` | `antiparasites[]` | `TreatmentPlan` + `TreatmentOccurrence` + `MedicalHistoryEntry` (`Prescription`). |
| `tblBloodTestResult`, `tblUrine`, `tblEtcTest`, `tblRadiology` | `labResults[]` | Attached as `LabResult` / `RadiologyRecord` on the pet's visit (or a dedicated historical lab visit) + `MedicalHistoryEntry` (`Lab`). |
| `tblAppointments` | `appointments[]` | `Appointment` (`fDate + fTime` → `startUtc`, `fIsCome` → checked-in state). |
| `tblCharge` + `tblChargeDetails` + `tblChargePay` | `invoices[]` (`lines[]`, `payments[]`) | Posted `Invoice` (`IRT` integer toman, whole-toman discount, insurer percentage) + settled `Payment` records. |
| `tblStockHead` + `tblStock` | `stockReceipts[]` (`lines[]`) | `Supplier` + posted `GoodsReceipt` + `StockMovement` (`MovementKind.Receipt`). |
| `tblHolyDays` | `holidays[]` | Holiday calendar entries. |
| `tblWare`, `tblWareDetail`, `tblConsumerStuffHead`, `tblConsumerStuff`, `tblBloodTest`, `tblDays`, `tblStaffDaysStatus` | `archive` | Preserved losslessly in bundle `archive` section. |
| Boarding (`پانسیون`) | *(none)* | Legacy DB has no boarding table; Animora boarding remains empty on import (documented in `notes[]`). |

---

## 4. Value & Encoding Rules

- **Dates:** Legacy Jalali strings (`yyyy/MM/dd` + optional `HH:mm`) are converted to Gregorian UTC (`YYYY-MM-DDTHH:MM:SSZ`, Tehran local noon `08:30:00Z` when date-only so local calendar date never shifts). Invalid or zero dates (`0000/00/00`, out-of-range month/day) become `null` and raw values are kept in `legacy`.
- **Money:** Plain integer toman (`IRT`, scale `0`) copied into `*Minor` fields without fractional scaling.
- **Phones:** Persian (`۰-۹`) and Arabic (`٠-٩`) digits are folded to ASCII; non-digit labels without digits become `null` (never fabricated).
- **Privacy & Git Hygiene:** Generated `*.animora.json` bundles and `*.bak` files contain clinic PII and are excluded via `.gitignore`. Never commit generated bundles or backup files to source control.
