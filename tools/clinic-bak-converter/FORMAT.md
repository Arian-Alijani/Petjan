# Clinic import bundle — format v2 (`clinic-import.animora.json`)

Contract: [`schema/clinic-import.schema.json`](schema/clinic-import.schema.json) (JSON Schema 2020-12).
The server re-validates `formatVersion == 2` and the structural rules below on upload.

## Conventions
| Topic | Rule | Origin (plan §4) |
|---|---|---|
| Keys | Every entity has `sourceId` (legacy `fId`, string). Source key = table + `sourceId`. Server derives ULIDs from `hash(tenantId \| table \| sourceId)` -> idempotent re-import. | §4.6 |
| Dates | UTC ISO-8601 `YYYY-MM-DDTHH:MM:SSZ`. Jalali `yyyy/MM/dd` (+ optional `HH:mm`) converted to Gregorian; invalid/zero dates -> `null`. | §4.4 |
| Money | Integer toman (`*Minor`, currency `IRT`, scale 0). | §4.5 |
| Phones | ASCII digits only; digitless -> `null`. | §4.7 |
| Enums | `species` Dog0 Cat1 Bird2 Rodent3 Reptile4 Other5 Rabbit6; `pet.gender` Unknown0 Male1 Female2; `pet.status` Active0 Inactive1 Deceased2; `owner.gender` Unspecified0 Male1 Female2. | §3.2/§4.2-3 |
| Lossless | Columns without an Animora home go to `legacy` (object) on the nearest entity. `--raw` adds top-level `raw` (table -> rows). | §4.8 |
| Boarding | No source data; never fabricated (see `notes`). | §3.2 |

## Top level
`formatVersion`(=2) `generatedUtc` `converterVersion` `sourceFingerprint`(sha256 of .bak) `sourceRowCounts` `dedupeRule` `currency`(=IRT) `notes[]` `warnings[]`
plus entity arrays below (all optional except `owners`).

| Array | Legacy source | Notes |
|---|---|---|
| `insurers` | tblInsurances | -> `sales.insurer.create` |
| `paymentTypes` | tblPaymentType | `method` inferred from name (cash/card/transfer/cheque/other) |
| `categories` | tblStuffGroup | -> `inventory.category.create` |
| `species` | tblAnimalType | original name -> PetSpecies map |
| `vaccineTypes` | tblVaccinationType | -> catalog |
| `staff` | tblStaff (+tblDesignation, tblSpecialty) | |
| `services` | tblServices | |
| `products` | tblStuff | `categorySourceId` -> categories |
| `owners[].pets[]` | tblPatient (+tblEnglishInfo, lookups) | one patient row = pet; owners de-duplicated (`dedupeRule`) |
| `visits` | tblPatientRecord + tblPaientRecordDetail(+Type) | detail `fType` 0 symptom, 2 treatment, 3 complaint |
| `vaccinations` | tblVaccination | `administeredUtc`=fDate, `nextDueUtc`=fNextDate |
| `antiparasites` | tblAntiParasite | |
| `labResults` | tblBloodTestResult, tblUrine, tblEtcTest, tblRadiology | `kind` + non-null analytes in `values` |
| `appointments` | tblAppointments | `isCheckedIn`=fIsCome |
| `invoices[].lines/payments` | tblCharge, tblChargeDetails, tblChargePay | line `kind` service/product by fxServices/fxStuff |
| `stockReceipts[].lines` | tblStockHead + tblStock | |
| `holidays` | tblHolyDays | |

Foreign keys are `*SourceId` strings that must resolve inside the same bundle; unresolved references are dropped into `warnings` by the converter and rejected by the server.
