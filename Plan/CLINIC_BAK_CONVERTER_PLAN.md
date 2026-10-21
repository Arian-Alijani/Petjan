# Plan — Legacy `Clinic.bak` → Animora Clinic Import Converter

> **This is a self-contained execution plan.** A fresh model can execute it WITHOUT re-reading
> `db-sample/Clinic.bak`, without re-reading the audit reports, and without re-deriving the schema.
> All facts already discovered are recorded below. Read §1–§6 once, then execute the task list in §7.
>
> **LOCKED RULE (do not break):** After completing EACH task in §7, run `scripts/git/push.sh "<msg>"`.
> Not pushing = project death. The final task deletes THIS file only.
>
> **Scope guard:** Investigate files/code only when a task needs it. No full builds, no long test runs,
> no aimless exploration. Keep generated artifacts optimal for AI models (low token, high clarity).

---

## 1. Goal (what we are building)

A **complete, standalone converter** that turns a legacy veterinary-clinic SQL Server backup
(`Clinic.bak`, file "A") into an Animora **clinic import bundle** (file "B") that the vendor admin
can upload in the admin panel to seed/attach data to ONE clinic (tenant). Clients of that clinic then
receive the data via the normal sync path.

Two deliverables:
1. **`tools/clinic-bak-converter/`** — a cross-platform Python converter + a Windows `.cmd` launcher.
   Input: a `*.bak` with the SAME internal schema as the sample. Output: `clinic-import.animora.json`
   (file B), a versioned, validated bundle that is 1:1 compatible with Animora's domain/command model.
2. **Admin-panel import feature** — a per-clinic page in `Animora.Web.Admin` (+ server endpoint +
   ingestion service) that accepts file B, validates it, and provisions the data into that clinic's
   tenant, isolated and idempotent.

**Design mandates (from the request):** standard, optimized, unified, exact, error-free; output must
match Animora's DB structure, coding/enum conventions, money/date encoding precisely; script runs on
Windows with a one-click `.cmd` entry point.

---

## 2. Source database "A" — `Clinic.bak` — FULLY ANALYZED (ground truth)

### 2.1 Physical format (CONFIRMED by opening the real file)
- `Clinic.bak` is a **Microsoft SQL Server native backup** in **MTF (Microsoft Tape Format)**.
  `file` reports: *"Windows NTbackup archive NT … Microsoft SQL Server"*. Size ≈ 10.5 MiB (11,031,040 bytes).
- The raw **MDF data stream is embedded** inside the MTF container. SQL Server pages are **8192 bytes**.
- **Proven page base offset = 7680** (`BASE=7680`); iterating `range(7680, len-8192+1, 8192)` yields
  **1345 page windows, of which 960 are live data pages** (header `page[:2]==b'\x01\x01'` and
  `u16(page,36)==1`). This technique already works in `scripts/extract-clinic-bak.py` (read-only page
  reader) — **reuse and EXTEND it to all tables**; the existing script only extracts owners+pets.
- Data-record parsing rules that already work (keep them):
  - Slot count at `u16(page,22)`; slot array grows down from page end (`u16(page, PAGE-2*(i+1))`).
  - Primary data record filter: `rec[0] & 16` set (has-null-bitmap), `rec[0] & 14 == 0` (not ghost/forwarded).
  - Fixed-length block ends at `u16(rec,2)`; then `count=u16(rec,fixed_end)`, null bitmap
    `(count+7)//8` bytes, then variable-length offset array, then variable-length payload.
  - **Do NOT guess** compressed/encrypted/LOB/row-overflow pages — reject a backup whose structure
    differs (defensive: verify `SOURCE_SHA256` of the schema-defining system pages, not the data).
- **Fingerprint of the sample schema:** `SOURCE_SHA256 = ae4affdd3a5d4c116ed8e55a7f10227970d4af029a7ab7d77b0262149b733414`
  (already a constant in both `extract-clinic-bak.py` and `ClinicBakPreviewData.cs`). Use it to detect
  "same schema family" but the converter must tolerate ANY row data, since later real backups have more rows.

### 2.2 Encoding & storage conventions (CONFIRMED from module text + data scan)
- **Text:** Persian strings are **NVARCHAR = UTF-16LE** (verified: owner surnames احمدی/اکبری/تنوری,
  animal types سگ/گربه/اسب/طوطی = dog/cat/horse/parrot, labels قیمت خرید/قیمت فروش, present in file).
- **Dates:** stored as **Jalali (Shamsi) strings** in `yyyy/MM/dd` format (verified in `VIEWMali`:
  `fDate >= '1388/01/01'`). Some date columns are `datetime`; treat string-Jalali as the primary case,
  with a secondary path for real SQL `datetime` (base 1900-01-01, day+time fractions).
- **Money:** plain numeric (toman), NO minor-unit scaling, NO decimals in practice (IRT scale 0).
- **Keys:** integer identities `fId`; foreign keys named `fx<Target>` (e.g. `fxPatient`, `fxCharge`).
- **Column naming:** every column is `f<Name>`; FKs `fx<Name>`; dates `f<...>Date`.

### 2.3 Complete table inventory (names recovered from system metadata + 38 views + 13 app procs)

> Mangled diagram artifacts cleaned: `tblClinicysi→tblClinic`, `tblHolyDaysi→tblHolyDays`,
> `tblPatientgy/tblPatientyp/tblPatientRe→tblPatient`, etc. `*_*` names like `tblCharge_tblPatient`
> are FK constraint names, NOT tables. The REAL base tables and their role:

**Patients / clinical core**
- `tblPatient` — the central record. **Mixes owner + pet in one row** (this is the key impedance mismatch
  vs Animora's split Owner/Pet). Columns (from `sp_SelectPatient`, authoritative):
  `fId, fFirstName, fLastName` (OWNER name), `fAnimalName, fAnimalType(→tblAnimalType.fId),
  fAnimalSex, fAnimalIsSterile, fAnimalBirthDate, fAnimalAge, fAnimalAgeType, fAnimalColor(→tblColors.fId),
  fAnimalBreed(→tblBreed.fId), fAnimalBloodClass, f1thVaccinationDate, f2thVaccinationDate,
  fRabiesVaccinationDate, fxDoctorId(→tblStaff), fxInsuranceId(→tblInsurances), fInsuranceNo,
  fTelNo, fMobileNo, fEmail, fAddress, fCity, fxDiseaseId, fDesc, fImage(blob/path), fDateCreate,
  fIdentity, fDescHistory, fxIdentifier(→tblIdentifier), fxBehaviorId, fxTypeBuyId, fxPlaceId,
  fxFoodId, fHasOperation, fHasTest, fCodeMicrochip, fDateKasht(microchip implant date), fSex(owner),
  fDateWaiting, fTimeWaiting, fBarcode, fNationalCode`.
- `tblEnglishInfo` — English/passport sidecar per patient (`fxPatientId`): `fMicrochipNo, fIDNo,
  fAnimalName, fSpecies, fAnimalBreed, fAnimalBirthDate, fAnimalBirthPlace, fAnimalSex, fAnimalColor,
  fOwner, fCity, fOwnershipDate, fTel, fAddress, fMicrochipDate, fDescription`.
- `tblAnimalType` (`fId,fName`) — species lookup (سگ/گربه/اسب/طوطی…). **Map to `PetSpecies` enum.**
- `tblBreed` (`fId,fName`), `tblColors` (`fId,fName`), `tblIdentifier` (`fId,fName`) — lookups.
- `tblInsurances` (`fId,fName`) — insurer lookup → Animora `sales.insurer.create`.
- `tblStaff` — `fId, fFirstName, fLastName` (+designation/specialty via `tblDesignation`,`tblSpecialty`).
- `tblBaseInfo` (`fId,fName`) — visit-reason lookup (used by `sp_AppointNexDate` as `fReason`).

**Medical records**
- `tblPatientRecord` — the visit/encounter: `fId, fTime, fxPatientId, fxDoctorId, fDate, fDiagnosis,
  fResults(prescription), fDateNext(next visit), fxReason(→tblBaseInfo)`.
- `tblPaientRecordDetail` (sic) — line items of a record: `fId, fxPatientRecordId, fxTypeId(→tblPetienRecordDetailType), fName`.
- `tblPetienRecordDetailType` (sic) — `fId, fType, fName`; `fType` codes seen via `GetTreatmentItems`:
  **0 = Symptom, 2 = Treatment, 3 = Complaints** (and diagnosis is a column on the record itself).
- `tblPatientRecordImage` (`fId, fImageDesc, fxPatientRecord`) and `tblPatientDocument`
  (`fId, fFile, fxPatientRecord, fxServiceType, fxPatientId, fDate, fExt`) — attachments.

**Vaccination & antiparasite (audit target emphasis)**
- `tblVaccination` — `fId, fxPatientId, fxVaccinationTypeId(→tblVaccinationType), fName, fDesc,
  fDate, fNextDate`. (from `sp_Vaccination`.)
- `tblVaccinationType` — `fId, fName`.
- `tblAntiParasite` — `fId, fDate, fxPatientId, fDesc, fInternalParasite, fExternalParasite, fNextDate`.
  (from `sp_AntiParasite`.)

**Appointments / scheduling (audit target emphasis)**
- `tblAppointments` — `fId, fxPatientId, fPatientName, fxDoctorId, fDate, fTime, fDesc, fMobile,
  fDateReal, fIsCome, fSMSTemplateSend`. (from `sp_AppointmentList`.)
- `tblDays`, `tblStaffDaysStatus`, `tblHolyDays` — working-day / holiday calendars (map to Animora
  staff working hours / scheduling calendar; may be empty in sample — still map them).

**Sales / accounting (audit target emphasis #11)**
- `tblCharge` — invoice header: `fId, fDate, fTime, fxPatient, fTotalCharge, fInsurancePay, fDiscount,
  fDiscountDetail, fPay, fxPaymentType(→tblPaymentType), fxInsurance, fInsuranceNo, fCharge, fIsDeptor,
  fDesc, fxUser, fNameFamily(walk-in name override), fxFactorType(→tblFactorType), fMobileNo,
  fReceiptNo, fTax, fIsNoTax, fFactorNo`.
- `tblChargeDetails` — invoice line: `fId, fxCharge, fxServices(→tblServices), fxStuff(→tblStuff),
  fxStaff(→tblStaff), fPrice, fQuantity, fTotalCharge, fCharge, fChargeMasrafi, fInsurancePay,
  fDiscountPrice, fDiscountPercent, fTaxDetail`.
- `tblChargePay` — payments against an invoice: `fId, fxCharge, fPrice` (+date/type inferred).
- `tblPaymentType` (`fId,fName`), `tblFactorType` (`fId,fName`) — lookups.
- `tblServices` — service catalog: `fId, fName, fPrice, fxStaffId`. (+`tblServicesInsurance` pricing.)

**Inventory / purchasing (audit target emphasis — "pension/پانسیون", stock)**
- `tblStuff` — product/goods: `fId, fName, fPrice(sale), fPriceBuy, fBarCode, fOrderPoint, fxStuffGroup(→tblStuffGroup)`.
- `tblStuffGroup` (`fId,fName`) — product category → Animora `inventory.category.create`.
- `tblStock` — stock movements/lots: `fId, fxStuff, fQuantity, fBuyPrice, fExpireDate, fDesc,
  fxStockHeadId(→tblStockHead), fDate`.
- `tblStockHead` — purchase/receipt header: `fId, fFactorNo, fFactorDate, fSeller, fTel, fDesc, fType,
  fxFactorType, fSellerCode, fxPaymentType, fTotalPrice, fTotalPay, fTotalDiscount, fTax`.
- `tblWare` / `tblWareDetail` — warehouse transfer docs (`fWareNo, fWareDate, fType`; detail:
  `fxStuff, fQuantity, fPrice, fStockCount, fDate, fxWare`).
- `tblConsumerStuffHead` / `tblConsumerStuff` — internal consumption: head `fxCharge, fxPatient, fDate`;
  line `fxStuff, fxStaff, fQuantity, fBuyPrice, fPrice, fTotalPrice, fTime`.
  **Note: "پانسیون/boarding" is NOT a dedicated table in the legacy DB** — boarding does not exist in
  source A. Animora boarding stays EMPTY on import (document this; do not fabricate).
- `tblDraft`/`tblDraftHead`, `tblCurrentCharge` — in-progress carts (transient; skip or map to nothing).

**Lab / diagnostics**
- `tblBloodTest` — reference-range template per animal type: columns `f<Analyte>Test` (e.g. `fHbTest,
  fRBCTest,…`, `fxAnimalTypeId`). This is the per-species normal-range catalog.
- `tblBloodTestResult` — actual results: `fId, fxPatientId, fDate, fTestType(1=Hematology,2=Biochemistry),
  fFile, fFileExt`, plus ~70 analyte columns: `fHet, fHb, fRBC, fMCV, fMCH, fMCHC, fNRBC, fRetic, fESR1h,
  fWBC, fDIFF, fSeg, fBand, fLymph, fMono, fEos, fBaso, fPlt, fTotalProtein, fAlbumin, fGlobulin,
  fFibrinogen, fGlocose, fTriglycerides, fCholesterol, fHDLCholesterol, fLDLCholesterol, fBilirubinTotal,
  fBilirobinDirect, fBUN, fUrea, fCreatinine, fAcidPhosphatase, fASTGOT, fALTGPT, fGGT, fCK, fLDH, fCa,
  fP, fMg, fFe, fTIBC, fLipase, fAmylase, fUricAcid, fALP, fOtherCells, fRDW, fK, fPt, fHbAlc, fNa, fCl,
  fPtt, fAGRatio, fBilirobinInDirect, fScr, fHct` + notes `fHemaNote/Report/Interpretation`,
  `fBioNote/Report/Interpretation`. (from `V_Hematology`/`V_Biochemistry`/`sp_BloodTestResult`.)
- `tblUrine` — urinalysis: `fId, fxPatientId, fDate, fNextVisitDate, fWeight, fPh, fGlucose, fCatonBadi,
  fNitrite, fBillyRubin, fAvrvbylynvzhn(urobilinogen), fHemoglobin, fHPFWhite, fHPFRed, fCrystal, fProtein`.
- `tblEtcTest` — other tests: `fId, fxPatientId, fTestName, fTestDate, fNextVisite, fResult`.
- `tblRadiology` — imaging per patient (`fxPatientId`).

**System / ignore**
- `sysdiagrams`, `sysdiagram_properties`, all `dt_*` procedures, `sp_creatediagram`/`sp_*diagram*`,
  `fn_diagramobjects` = SQL Server Database-Diagram / VSS source-control plumbing. **Ignore entirely.**

### 2.4 Views & stored procedures (reference for exact joins — already extracted, do not re-open .bak)
- Reporting views reveal exact FK joins: `V_Charge`, `V_ChargeDep`, `V_ChargeMain`, `V_ChargeAll`,
  `VIEWFactor`, `sp_Factor`, `sp_ChargeDetail` (sales); `V_Stock`, `V_StuffKardex*`, `sp_Stock`,
  `sp_StockHead`, `sp_BuyFactorAll`, `sp_WareHead` (inventory); `sp_Vaccination`, `sp_AntiParasite`
  (immuno); `sp_AppointmentList`, `sp_AppointNexDate` (scheduling); `V_Hematology`, `V_Biochemistry`,
  `V_Urine`, `V_EtcTest`, `sp_BloodTestResult` (lab); `sp_SelectPatient`, `sp_PatientRecord`,
  `GetTreatmentItems` (clinical). The authoritative column lists in §2.3 were derived from these.

---

## 3. Target system "B" — Animora clinic model — FULLY ANALYZED (ground truth)

Animora = multi-tenant SaaS vet clinic + accounting. **.NET 10, DDD + CQRS commands**, offline-first
Avalonia desktop syncing to one ASP.NET Core server. One customer = one **account + one clinic (tenant)**.

### 3.1 Data goes in via COMMANDS, not raw rows (critical architectural fact)
- The write model is a fixed command vocabulary in
  `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` (**94 types**). The import bundle must be
  translatable into these commands (or into the equivalent domain-aggregate seeding the server applies).
- **Ids are deterministic ULIDs.** `IdGenerator.NewId(instant)` (monotonic Crockford ULID) and
  `IdGenerator.ForOperation(opId,…)` (deterministic child ids so client & server mint identical ids →
  sync idempotency). The converter must NOT invent integer ids; it emits stable **source keys**
  (e.g. legacy `fId`) and the ingestion service maps each source key → a ULID deterministically
  (hash the tenantId + table + legacy fId) so re-import is idempotent and FKs resolve.
- Money = `Money(long Minor, string Currency)`, all arithmetic `checked`, single rounding boundary
  `ApplyPercent` (MidpointRounding.AwayFromZero). Currency for Iran = IRT, **scale 0** → `Minor == toman`.
- Time: `IClock.UtcNow` is the only clock; all stored dates are `DateTimeOffset` UTC. **Jalali source
  strings must be converted to Gregorian UTC** via `System.Globalization.PersianCalendar`.

### 3.2 Relevant target aggregates / commands (the mapping endpoints)
- **Owner** (`src/Animora.Domain/Patients/Owner.cs`): `FullName, PhonePrimary, PhoneSecondary,
  PhoneLandline, Referral, NationalId, Email, Address, City, Gender(OwnerGender), Notes, RegisteredUtc,
  IsBlocked`. `Owner.Register(tenantId, fullName, phone?, clock, gender, id?)` then `UpdateContactDetails(...)`.
  **An absent phone is allowed** — pass `null`, stored as empty string ("not recorded"); NEVER invent a
  phone and NEVER pass a digitless label (the domain refuses digitless non-blank values). Commands:
  `patients.owner.create`, `patients.owner.update`.
- **Pet** (`src/Animora.Domain/Patients/Pet.cs`): `OwnerId, Name, Species(PetSpecies), Breed, Gender(PetGender),
  BirthDateUtc, IsNeutered, Color, MicrochipCode, MicrochipImplantUtc(+vendor/location), Habitat, Behavior,
  Diet, BloodType, Insurance, NeuteredUtc, Status(PetStatus), AllergyNotes, ChronicNotes, RegisteredUtc`.
  **`Species` is immutable after creation** — set it right the first time. `Pet.Register(...)`,
  `pet.UpdateDetails(...)`, `SetMicrochip(code, implantedUtc, vendor, location)` (implant date MANDATORY if
  code present → if source has code but no date, keep code in a note, don't invent a date), `SetStatus(...)`.
  Commands: `patients.pet.create`, `patients.pet.update`, `patients.pet.weight.add`, `patients.pet.document.add`.
  Enums (persisted integer values — DO NOT shift):
  - `PetSpecies`: Dog=0, Cat=1, Bird=2, Rodent=3, Reptile=4, Other=5, Rabbit=6.
  - `PetGender`: Unknown=0, Male=1, Female=2. `PetStatus`: Active=0, Inactive=1, Deceased=2.
  - `OwnerGender`: Unspecified=0, Male=1, Female=2.
- **Vaccination** (`vaccination.record.register` / `.administer` / `.reminder`, `vaccination.catalog.add`,
  and the newer `vaccination.treatment.*` family for scheduled multi-dose plans + `Clinical.TreatmentPlan`).
  Map `tblVaccination`→vaccination records (fDate=administered, fNextDate=reminder), `tblVaccinationType`→catalog.
  Map `tblAntiParasite`→treatment records (internal/external parasite as treatment items; fNextDate=reminder).
- **Scheduling** (`scheduling.appointment.book/move/cancel/checkin`, `scheduling.bookingpatient.create`).
  Map `tblAppointments` (fDate+fTime→start, fxDoctorId→staff, fIsCome→checkin, fPatientName override→walk-in).
- **Sales / accounting**: `sales.invoice.create` + `sales.invoice.line.add` + `sales.invoice.discount.set`
  + `sales.invoice.insurance.set` + `sales.invoice.post`, `sales.payment.register`, `sales.insurer.create`.
  Map `tblCharge`→invoice, `tblChargeDetails`→lines (service vs stuff), `tblChargePay`→payments,
  `tblInsurances`→insurers, `tblPaymentType`→payment method. **Respect `InvoiceTotals.Compute` rules**
  (fixed discount must be whole minor units; see DOM-02 — pass integer toman).
- **Inventory / purchasing**: `inventory.product.create` (+`inventory.variant.add`), `inventory.category.create`,
  `inventory.receipt.post` / `purchasing.receipt.record`, `inventory.stock.set/adjust`.
  Map `tblStuff`→product, `tblStuffGroup`→category, `tblStock`+`tblStockHead`→receipt/stock, `tblServices`→
  service products (or a services catalog — verify where services live in Animora during the mapping task).
- **Visits / lab**: `visits.visit.start/note/complete`, `visits.lab.add`, `visits.radiology.add`,
  `visits.rx.add`. Map `tblPatientRecord`→visit (fDiagnosis, fResults=rx, detail items=symptoms/treatments),
  `tblBloodTestResult`/`tblUrine`/`tblEtcTest`→lab entries, `tblRadiology`→radiology.
- **Boarding (پانسیون)**: NO source data exists in A → leave empty. Document in bundle metadata.

### 3.3 How data is physically stored on the client (for validation only)
- Local store: encrypted SQLite, EF Core, migrations `001`–`033` (head `033_OnlineEntitlement`).
  Server: PostgreSQL (Npgsql), migrations `101`–`111`. Tenant isolation is defence-in-depth: every
  `SyncableEntity` is tenant-filtered + tenant-stamped-from-context; writes under the wrong tenant throw
  `TenantMismatchException`. **The import MUST go through the tenant-bound ingestion path** so stamping
  applies; never write rows directly bypassing the tenant context.
- There is already a **preview-only** path: `scripts/extract-clinic-bak.py` → JSON → seed
  `src/Animora.Persistence.Sqlite/Seed/ClinicBakPreviewData.cs` (`SeedKey="clinic-bak.preview.v1"`,
  `FormatVersion==1`, only Owners+Pets+microchip, requires an EMPTY isolated store). **This is the
  starting contract to supersede** — the new bundle is a superset (all tables) and imports into an
  existing tenant via the admin panel, not just a local empty-store preview.

---

## 4. Mapping A → B (the decisive, pre-computed decisions)

> These decisions are already made so the executing model does not re-derive them.

1. **Split `tblPatient` into Owner + Pet.** One legacy patient row = 1 Owner (fFirstName+fLastName,
   fTelNo/fMobileNo, fEmail, fAddress, fCity, fNationalCode→NationalId, fSex→OwnerGender) + 1 Pet
   (fAnimalName, fAnimalType→Species, fAnimalBreed→Breed text, fAnimalSex→PetGender,
   fAnimalBirthDate→BirthDateUtc, fAnimalIsSterile→IsNeutered, fAnimalColor→Color,
   fCodeMicrochip+fDateKasht→microchip, fAnimalBloodClass→BloodType, fxFoodId→Diet(lookup),
   fxBehaviorId→Behavior(lookup), fxPlaceId→Habitat(lookup), fxInsuranceId→Insurance).
   - **De-duplicate owners**: many legacy patient rows share one human owner. Group by a normalized key
     (national code if present, else normalized name+primary phone). Each distinct human → one Owner;
     each patient row → one Pet under that Owner. Record the dedupe rule in bundle metadata.
2. **Species map** `tblAnimalType.fName` → `PetSpecies`: سگ→Dog, گربه→Cat, پرنده/طوطی/مرغ→Bird,
   موش/همستر/خرگوش-small→Rodent (خرگوش→Rabbit=6 specifically), خزنده/لاک‌پشت/مار→Reptile, اسب/گاو/other→Other.
   Keep the ORIGINAL Persian type name in `Breed`/notes so nothing is lost even when folded to `Other`.
3. **Gender maps**: animal sex نر→Male, ماده→Female, else Unknown. Owner sex same mapping→OwnerGender.
4. **Dates**: every Jalali `yyyy/MM/dd` string → Gregorian `DateTimeOffset` UTC via `PersianCalendar`.
   Invalid/partial Jalali (e.g. `0000/00/00`, empty) → null (do not fabricate). Pair `fDate`+`fTime` for
   appointments/invoices. Keep a `source_date_raw` in metadata where ambiguity exists.
5. **Money**: copy numeric toman verbatim into `Minor` with currency IRT (scale 0). Round only through the
   domain; the bundle carries integer toman. Fixed discounts MUST be whole toman (DOM-02).
6. **Deterministic id mapping**: bundle emits `source_table` + `source_id` for every entity; the ingestion
   service derives the ULID as `IdGenerator.ForOperation`-style deterministic id from
   `hash(tenantId | source_table | source_id)` so FKs resolve and re-import is idempotent (upsert by source key).
7. **Phones**: normalize Persian/Arabic digits→ASCII (domain `Owner.NormalizePhone` logic). Digitless →
   treat as absent (null), never pass a digitless non-blank value.
8. **Lossless guarantee**: any legacy column with no Animora home goes into a structured `legacy` JSON
   note on the nearest entity (and the bundle keeps a `raw` section per table behind a flag) so "all of A
   is present in B" holds. Nothing silently dropped.
9. **Empty tables**: tables empty in the sample (possibly populated in future backups) are STILL mapped in
   the converter (boarding has no source; `tblDays`/`tblHolyDays`/`tblWare` etc. mapped when present).

---

## 5. Output bundle "B" format (contract — design in task 2, keep stable)

A single UTF-8 JSON file `clinic-import.animora.json`:
- `formatVersion` (int, start at `2` to distinguish from the preview v1), `generatedUtc`,
  `converterVersion`, `sourceFingerprint` (sha256 of schema pages), `sourceRowCounts` per table,
  `dedupeRule`, `warnings[]`.
- `insurers[]`, `paymentTypes[]`, `categories[]`, `species[]`(original→enum map), `staff[]`,
  `services[]`, `products[]`, `owners[]`→ each with `pets[]`, and per entity the clinical/financial
  children keyed by source id: `visits[]`, `vaccinations[]`, `antiparasites[]`, `labResults[]`,
  `appointments[]`, `invoices[]`(+lines+payments), `stockReceipts[]`.
- Every record carries `sourceTable`, `sourceId`, and (where lossy) `legacy` passthrough.
- Bundle is validated by a JSON Schema the ingestion service also enforces server-side.

---

## 6. Risks / guardrails already identified
- **No SQL Server / Docker in sandbox** → sandbox dev uses the stdlib page-reader approach (BASE=7680).
  On Windows the converter should ALSO support an optional fast path via `SqlLocalDB`/`pyodbc` if present,
  but the stdlib page reader is the guaranteed-portable default (no external install for the user).
- Row-overflow / LOB (`fImage`, big notes) may spill off-page — detect and either follow the pointer or
  record a warning; never emit corrupt text.
- Re-import must be idempotent (deterministic ids + upsert by source key) so an admin can re-run safely.
- Tenant isolation: ingestion runs on a dedicated tenant-bound scope (ISO-04); stamping must apply.
- Do not regress the existing preview path until the new path is proven; keep `FormatVersion` distinct.

---

## 7. TASK LIST (execute in order; PUSH after EACH task)

> Status legend: `[ ]` todo · `[~]` in progress · `[x]` done. Update this file as you go and push it too.
> After each task: `scripts/git/push.sh "feat(import): <task summary>"`. If push exits 3 →
> call `setup_github_environment` tool once, re-run. If exit 2 → resolve rebase, re-run.

- [x] **T1 — Scaffold converter project.** Create `tools/clinic-bak-converter/` with: `converter/`
  (package), `run-converter.cmd` (Windows one-click: prompts for input `.bak` + output path, calls Python),
  `README.md` (usage, Windows + fallback), `requirements.txt` (stdlib-only default; optional pyodbc extra).
  No logic yet beyond CLI argparse skeleton (`--bak`, `--out`, `--raw`, `--verbose`). PUSH.

- [x] **T2 — Define bundle schema (file B contract).** Write `tools/clinic-bak-converter/schema/clinic-import.schema.json`
  (JSON Schema draft 2020-12) exactly per §5, `formatVersion=2`. Add a short `FORMAT.md` documenting every
  field and its §4 mapping origin. PUSH.

- [x] **T3 — MDF page reader core.** In `converter/mdf_reader.py`: port+generalize the proven reader
  (BASE=7680, 8192 pages, slot/record parsing from §2.1) into a reusable "read all rows of a named table"
  API. Resolve the system catalog (object id → table name → column order/types) from the MDF system pages
  so columns bind by name, not guesswork. Add row-overflow/LOB detection with warnings. Unit-smoke against
  `db-sample/Clinic.bak` (expect 960 data pages). Keep it stdlib-only. PUSH.

- [x] **T4 — Typed table extractors.** In `converter/tables.py`: one typed reader per source table in §2.3
  (Patient, EnglishInfo, lookups, PatientRecord(+detail+type), Vaccination(+type), AntiParasite, Appointments,
  Charge(+details+pay), PaymentType, FactorType, Services, Stuff(+group), Stock(+head), Ware(+detail),
  ConsumerStuff(+head), BloodTest(+result), Urine, EtcTest, Radiology, Staff, Days/HolyDays). Each returns
  dict rows keyed by the §2.3 column names. Handle UTF-16LE text, numeric, and date columns. PUSH.

- [x] **T5 — Value converters.** In `converter/convert.py`: Jalali→Gregorian UTC (`PersianCalendar`-equivalent
  pure-python, or use `jdatetime` only if you vendor it — prefer pure python, no network), Persian/Arabic
  digit folding, phone normalization (mirror `Owner.NormalizePhone`), species/gender/status enum maps (§4),
  money passthrough (IRT scale 0). Deterministic source-key → stable id helper. Pure functions, unit-smoke. PUSH.

- [x] **T6 — Mapping engine (A→B).** In `converter/mapping.py`: implement §4 decisions — owner dedupe,
  patient→owner+pet split, all clinical/financial/inventory children attached by source id, lossless `legacy`
  passthrough, warnings. Emit the in-memory bundle object matching the T2 schema. PUSH.

- [ ] **T7 — Writer + validation + CLI wiring.** `converter/bundle.py` serializes to
  `clinic-import.animora.json`; validate against the T2 JSON Schema before writing; print a summary
  (counts per entity, warnings). Wire `__main__.py` and `run-converter.cmd` end-to-end. Run it against the
  sample `.bak` and confirm a valid bundle is produced with non-zero owners/pets. PUSH.
  **(If this task produced a large amount of code, this push is the heavy-code checkpoint.)**

- [ ] **T8 — Server import contract + ingestion service.** Add a server-side import DTO + endpoint under
  `src/Animora.Server/Admin/` (operator-cookie protected, antiforgery, size-limited, matching the backup
  endpoints' hardening) that accepts bundle B for a given tenant id, validates `formatVersion==2` + schema,
  and hands to an ingestion service that, on a DEDICATED tenant-bound scope, materializes owners/pets/…
  via the domain aggregates/commands with deterministic ids (idempotent upsert by source key). Reuse the
  tenant-stamping path (no raw writes). Keep the preview path intact. PUSH (heavy-code checkpoint if large).

- [ ] **T9 — Admin panel per-clinic import page.** Add `src/Animora.Web.Admin/Pages/TenantImport.razor`
  (linked from `TenantDetail.razor`): upload file B, show validation summary + warnings, confirm, submit to
  the T8 endpoint, show result. Follow existing admin page conventions (localization strings, StatusBadge,
  FormError, ConfirmCheckbox, RenderModePolicy). PUSH.

- [ ] **T10 — End-to-end verification (light).** Convert the sample `.bak`, load the bundle through the
  ingestion service against a throwaway tenant in a targeted test (NO full solution build / NO long test run —
  a focused unit/integration test on the ingestion service + converter smoke only). Fix issues; if a heavy
  fix lands mid-task, PUSH that fix immediately with a clear message. Final PUSH.

- [ ] **T11 — Docs + cleanup.** Update `tools/clinic-bak-converter/README.md` with the full Windows workflow
  (double-click `run-converter.cmd` → pick `.bak` → get bundle → upload in admin panel per clinic) and the
  A→B mapping table (condensed §4). Ensure no PII and no `.bak`-derived data is committed. PUSH.

- [ ] **T12 — FINAL: delete this plan file.** `git rm Plan/CLINIC_BAK_CONVERTER_PLAN.md` (delete ONLY this
  file — do not touch other files, reports, or any roadmap). PUSH.
