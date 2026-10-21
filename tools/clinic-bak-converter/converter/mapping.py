"""A -> B mapping engine (plan §4): legacy tables -> import bundle (format v2).

Pure function of :class:`~converter.tables.LegacyTables`; no I/O. Decisions
(owner de-duplication, patient split, FK resolution, lossless ``legacy``
passthrough) are documented in ``FORMAT.md``.
"""
from __future__ import annotations

import collections
import datetime as _dt
from typing import Any

from . import BUNDLE_FORMAT_VERSION, __version__
from .convert import (clean_text, fold_digits, map_animal_sex, map_owner_sex, map_payment_method,
                      map_species, money_minor, normalize_phone, to_utc)
from .tables import BLOOD_ANALYTES, LegacyTables

DEDUPE_RULE = ("owner = valid 10-digit national id (not all-same-digit); else normalized full name + primary phone; "
               "else one owner per patient row. One tblPatient row = one pet.")
STATIC_NOTES = [
    "Boarding (پانسیون) has no source table in the legacy database; Animora boarding stays empty.",
    "Money is carried as whole toman (IRT, scale 0).",
    "Dates: Jalali converted to UTC assuming Iran standard time (UTC+03:30); date-only values map to local noon.",
]
ARCHIVE_TABLES = ("tblWare", "tblWareDetail", "tblConsumerStuffHead", "tblConsumerStuff", "tblBloodTest",
                  "tblStaffDaysStatus", "tblDays")
VISIT_DETAIL_KIND = {0: "symptoms", 2: "treatments", 3: "complaints"}
HEMA_NOTES = ("fHemaNote", "fHemaReport", "fHemaInterpretation")
BIO_NOTES = ("fBioNote", "fBioReport", "fBioInterpretation")
MAX_WARNING_IDS = 5


class _Ctx:
    """Collects aggregated warnings (message -> count + a few sample ids)."""

    def __init__(self) -> None:
        self.counts: collections.Counter[str] = collections.Counter()
        self.samples: dict[str, list[str]] = collections.defaultdict(list)
        self.binary_columns: set[str] = set()

    def warn(self, message: str, sample: Any = None) -> None:
        self.counts[message] += 1
        if sample is not None and len(self.samples[message]) < MAX_WARNING_IDS:
            self.samples[message].append(str(sample))

    def render(self) -> list[str]:
        out = []
        for message, n in sorted(self.counts.items()):
            ids = self.samples.get(message)
            out.append(f"{message} (x{n}" + (f"; e.g. ids {', '.join(ids)}" if ids else "") + ")")
        for col in sorted(self.binary_columns):
            out.append(f"binary column {col} omitted from bundle (not representable in JSON)")
        return out


def _sid(value: Any) -> str | None:
    return None if value is None else str(value)


def _legacy(ctx: _Ctx, table: str, row: dict[str, Any], used: set[str]) -> dict[str, Any] | None:
    """Columns of ``row`` not consumed by the mapping (lossless passthrough)."""
    out: dict[str, Any] = {}
    for key, value in row.items():
        if key in used or value is None or value == "":
            continue
        if isinstance(value, dict):  # undecodable binary / blob
            ctx.binary_columns.add(f"{table}.{key}")
            continue
        out[key] = value
    return out or None


def _join(*parts: Any) -> str | None:
    texts = [t for t in (clean_text(p) for p in parts) if t]
    return "\n".join(texts) or None


def _valid_national_id(value: Any) -> str | None:
    digits = "".join(c for c in fold_digits(str(value or "")) if c.isdigit())
    if len(digits) == 10 and len(set(digits)) > 1:
        return digits
    return None


def _full_name(first: Any, last: Any) -> str | None:
    return clean_text(f"{first or ''} {last or ''}")


def _resolve(ctx: _Ctx, what: str, value: Any, known: set[int] | dict, row_id: Any) -> str | None:
    """FK -> source id string if it resolves inside the bundle, else None (+warning)."""
    if value is None or value == 0:
        return None
    if value in known:
        return str(value)
    ctx.warn(f"{what}: unresolved reference set to null", row_id)
    return None


# ===================================================================== lookups
def _map_lookups(t: LegacyTables, ctx: _Ctx, bundle: dict[str, Any]) -> None:
    def simple(table: str) -> list[dict[str, Any]]:
        return [{"sourceId": str(r["fId"]), "name": r["fName"],
                 "legacy": _legacy(ctx, table, r, {"fId", "fName"})}
                for r in t.get(table) if r.get("fId") is not None and r.get("fName")]

    bundle["insurers"] = simple("tblInsurances")
    bundle["categories"] = simple("tblStuffGroup")
    bundle["vaccineTypes"] = simple("tblVaccinationType")
    bundle["paymentTypes"] = [
        {"sourceId": str(r["fId"]), "name": r["fName"], "method": map_payment_method(r["fName"]),
         "legacy": _legacy(ctx, "tblPaymentType", r, {"fId", "fName"})}
        for r in t.get("tblPaymentType") if r.get("fId") is not None and r.get("fName")]
    bundle["species"] = [
        {"sourceId": str(r["fId"]), "name": r["fName"], "species": map_species(r["fName"]),
         "legacy": _legacy(ctx, "tblAnimalType", r, {"fId", "fName"})}
        for r in t.get("tblAnimalType") if r.get("fId") is not None and r.get("fName")]
    designations, specialties = t.lookup("tblDesignation"), t.lookup("tblSpecialty")
    bundle["staff"] = []
    for r in t.get("tblStaff"):
        name = _full_name(r["fFirstName"], r["fLastName"])
        if r.get("fId") is None or not name:
            ctx.warn("tblStaff: row without id or name skipped", r.get("fId"))
            continue
        bundle["staff"].append({
            "sourceId": str(r["fId"]), "fullName": name,
            "designation": designations.get(r.get("fxDesignation")),
            "specialty": specialties.get(r.get("fxSpecialty")),
            "legacy": _legacy(ctx, "tblStaff", r, {"fId", "fFirstName", "fLastName", "fxDesignation", "fxSpecialty"})})


def _map_catalog(t: LegacyTables, ctx: _Ctx, bundle: dict[str, Any]) -> None:
    staff_ids = {int(s["sourceId"]) for s in bundle["staff"]}
    cat_ids = {int(c["sourceId"]) for c in bundle["categories"]}
    bundle["services"] = []
    for r in t.get("tblServices"):
        if r.get("fId") is None or not r.get("fName"):
            ctx.warn("tblServices: row without id or name skipped", r.get("fId"))
            continue
        bundle["services"].append({
            "sourceId": str(r["fId"]), "name": r["fName"], "priceMinor": money_minor(r["fPrice"]),
            "staffSourceId": _resolve(ctx, "tblServices.fxStaffId", r.get("fxStaffId"), staff_ids, r["fId"]),
            "legacy": _legacy(ctx, "tblServices", r, {"fId", "fName", "fPrice", "fxStaffId"})})
    bundle["products"] = []
    for r in t.get("tblStuff"):
        if r.get("fId") is None or not r.get("fName"):
            ctx.warn("tblStuff: row without id or name skipped", r.get("fId"))
            continue
        bundle["products"].append({
            "sourceId": str(r["fId"]), "name": r["fName"], "salePriceMinor": money_minor(r["fPrice"]),
            "buyPriceMinor": money_minor(r["fPriceBuy"]), "barcode": clean_text(r["fBarCode"]),
            "reorderPoint": r["fOrderPoint"],
            "categorySourceId": _resolve(ctx, "tblStuff.fxStuffGroup", r.get("fxStuffGroup"), cat_ids, r["fId"]),
            "legacy": _legacy(ctx, "tblStuff", r, {"fId", "fName", "fPrice", "fPriceBuy", "fBarCode",
                                                   "fOrderPoint", "fxStuffGroup"})})


# ==================================================================== patients
PATIENT_USED = {"fId", "fFirstName", "fLastName", "fAnimalName", "fAnimalType", "fAnimalSex", "fAnimalIsSterile",
                "fAnimalBirthDate", "fAnimalColor", "fAnimalBreed", "fAnimalBloodClass", "fxInsuranceId",
                "fInsuranceNo", "fTelNo", "fMobileNo", "fEmail", "fAddress", "fCity", "fDesc", "fDescHistory",
                "fDateCreate", "fCodeMicrochip", "fDateKasht", "fSex", "fNationalCode", "fIsInActive"}


def _map_patients(t: LegacyTables, ctx: _Ctx, bundle: dict[str, Any]) -> dict[int, dict[str, Any]]:
    types, breeds, colors = t.lookup("tblAnimalType"), t.lookup("tblBreed"), t.lookup("tblColors")
    insurers = t.lookup("tblInsurances")
    english = {r["fxPatientId"]: r for r in t.get("tblEnglishInfo") if r.get("fxPatientId") is not None}
    owners: dict[Any, dict[str, Any]] = {}
    pets: dict[int, dict[str, Any]] = {}
    seen: set[int] = set()
    for r in sorted((r for r in t.get("tblPatient")), key=lambda x: (x.get("fId") is None, x.get("fId") or 0)):
        pid = r.get("fId")
        if pid is None or pid in seen:
            ctx.warn("tblPatient: row without unique id skipped", pid)
            continue
        seen.add(pid)
        owner_name = _full_name(r["fFirstName"], r["fLastName"])
        mobile, tel = normalize_phone(r["fMobileNo"]), normalize_phone(r["fTelNo"])
        primary = mobile or tel
        national = _valid_national_id(r["fNationalCode"])
        if not owner_name:
            ctx.warn("tblPatient: owner name missing, placeholder used", pid)
            key: Any = ("id", pid)
            owner_name = "مالک نامشخص"
        elif national:
            key = ("nid", national)
        elif primary:
            key = ("np", owner_name, primary)
        else:
            key = ("id", pid)
        owner = owners.get(key)
        if owner is None:
            owner = owners[key] = {
                "sourceId": f"owner:{pid}", "fullName": owner_name, "phonePrimary": None, "phoneSecondary": None,
                "phoneLandline": None, "nationalId": None, "email": None, "address": None, "city": None,
                "gender": 0, "registeredUtc": None, "notes": None, "pets": [], "legacy": None}
        owner["gender"] = owner["gender"] or map_owner_sex(r["fSex"])
        for field, value in (("nationalId", national or clean_text(r["fNationalCode"])), ("email", clean_text(r["fEmail"])),
                             ("address", clean_text(r["fAddress"])), ("city", clean_text(r["fCity"]))):
            owner[field] = owner[field] or value
        if mobile:
            if owner["phonePrimary"] is None:
                owner["phonePrimary"] = mobile
            elif mobile != owner["phonePrimary"]:
                owner["phoneSecondary"] = owner["phoneSecondary"] or mobile
        if tel:
            if owner["phonePrimary"] is None:
                owner["phonePrimary"] = tel
            elif tel != owner["phonePrimary"]:
                owner["phoneLandline"] = owner["phoneLandline"] or tel
        registered = to_utc(r["fDateCreate"])
        if registered and (owner["registeredUtc"] is None or registered < owner["registeredUtc"]):
            owner["registeredUtc"] = registered

        type_name = types.get(r["fAnimalType"])
        species = map_species(type_name)
        breed = breeds.get(r["fAnimalBreed"])
        if species == 5 and type_name:  # folded to Other: keep the original Persian type name
            breed = f"{type_name} / {breed}" if breed else type_name
        eng = english.get(pid) or {}
        legacy = _legacy(ctx, "tblPatient", r, PATIENT_USED) or {}
        chip = clean_text(r["fCodeMicrochip"]) or clean_text(eng.get("fMicrochipNo"))
        implant = to_utc(r["fDateKasht"]) or to_utc(eng.get("fMicrochipDate"))
        if chip and not implant:
            legacy["microchipCode"] = chip  # domain requires an implant date; never invent one
            chip = None
        if eng:
            info = _legacy(ctx, "tblEnglishInfo", eng, {"fId", "fxPatientId"})
            if info:
                legacy["englishInfo"] = info
        insurance_name = insurers.get(r["fxInsuranceId"])
        insurance = _join(insurance_name, f"شماره بیمه: {r['fInsuranceNo']}" if r["fInsuranceNo"] else None)
        pet = {
            "sourceId": str(pid), "name": clean_text(r["fAnimalName"]) or "بدون نام", "species": species,
            "speciesName": type_name, "breed": breed, "gender": map_animal_sex(r["fAnimalSex"]),
            "birthDateUtc": to_utc(r["fAnimalBirthDate"]), "isNeutered": bool(r["fAnimalIsSterile"]),
            "color": colors.get(r["fAnimalColor"]), "microchipCode": chip,
            "microchipImplantUtc": implant if chip else None, "bloodType": clean_text(r["fAnimalBloodClass"]),
            "insurance": insurance, "status": 1 if r["fIsInActive"] else 0, "registeredUtc": registered,
            "notes": _join(r["fDesc"], r["fDescHistory"]), "legacy": legacy or None}
        if not clean_text(r["fAnimalName"]):
            ctx.warn("tblPatient: pet name missing, placeholder used", pid)
        owner["pets"].append(pet)
        pets[pid] = pet
    bundle["owners"] = list(owners.values())
    return pets


# ================================================================== clinical
def _map_clinical(t: LegacyTables, ctx: _Ctx, bundle: dict[str, Any], pets: dict[int, dict[str, Any]]) -> None:
    staff_ids = {int(s["sourceId"]) for s in bundle["staff"]}
    reasons = t.lookup("tblBaseInfo")
    detail_types = {r["fId"]: r.get("fType") for r in t.get("tblPetienRecordDetailType") if r.get("fId") is not None}
    details = t.group("tblPaientRecordDetail", "fxPatientRecordId")

    def pet_id(table: str, value: Any, row_id: Any) -> str | None:
        if value in pets:
            return str(value)
        ctx.warn(f"{table}: row dropped, patient not found", row_id)
        return None

    bundle["visits"] = []
    for r in t.get("tblPatientRecord"):
        pid = pet_id("tblPatientRecord", r["fxPatientId"], r["fId"])
        if pid is None or r.get("fId") is None:
            continue
        buckets: dict[str, list[str]] = {"symptoms": [], "treatments": [], "complaints": []}
        extra: list[dict[str, Any]] = []
        for d in details.get(r["fId"], []):
            kind = VISIT_DETAIL_KIND.get(detail_types.get(d["fxTypeId"]))
            if kind and d["fName"]:
                buckets[kind].append(d["fName"])
            elif d["fName"]:
                extra.append({"type": detail_types.get(d["fxTypeId"]), "name": d["fName"]})
        legacy = _legacy(ctx, "tblPatientRecord", r, {"fId", "fTime", "fxPatientId", "fxDoctorId", "fDate",
                                                     "fDiagnosis", "fResults", "fDateNext", "fxReason"}) or {}
        if extra:
            legacy["otherDetails"] = extra
        bundle["visits"].append({
            "sourceId": str(r["fId"]), "petSourceId": pid,
            "doctorSourceId": _resolve(ctx, "tblPatientRecord.fxDoctorId", r["fxDoctorId"], staff_ids, r["fId"]),
            "visitedUtc": to_utc(r["fDate"], r["fTime"]), "diagnosis": r["fDiagnosis"], "prescription": r["fResults"],
            "nextVisitUtc": to_utc(r["fDateNext"]), "reason": reasons.get(r["fxReason"]), **buckets,
            "legacy": legacy or None})

    vtypes = {int(v["sourceId"]): v["name"] for v in bundle["vaccineTypes"]}
    bundle["vaccinations"] = []
    for r in t.get("tblVaccination"):
        pid = pet_id("tblVaccination", r["fxPatientId"], r["fId"])
        if pid is None or r.get("fId") is None:
            continue
        bundle["vaccinations"].append({
            "sourceId": str(r["fId"]), "petSourceId": pid,
            "vaccineTypeSourceId": _resolve(ctx, "tblVaccination.fxVaccinationTypeId", r["fxVaccinationTypeId"], vtypes, r["fId"]),
            "name": r["fName"] or vtypes.get(r["fxVaccinationTypeId"]), "notes": r["fDesc"],
            "administeredUtc": to_utc(r["fDate"]), "nextDueUtc": to_utc(r["fNextDate"]),
            "legacy": _legacy(ctx, "tblVaccination", r, {"fId", "fxPatientId", "fxVaccinationTypeId", "fName", "fDesc",
                                                         "fDate", "fNextDate"})})
    bundle["antiparasites"] = []
    for r in t.get("tblAntiParasite"):
        pid = pet_id("tblAntiParasite", r["fxPatientId"], r["fId"])
        if pid is None or r.get("fId") is None:
            continue
        bundle["antiparasites"].append({
            "sourceId": str(r["fId"]), "petSourceId": pid, "administeredUtc": to_utc(r["fDate"]), "notes": r["fDesc"],
            "internalParasite": r["fInternalParasite"], "externalParasite": r["fExternalParasite"],
            "nextDueUtc": to_utc(r["fNextDate"]),
            "legacy": _legacy(ctx, "tblAntiParasite", r, {"fId", "fDate", "fxPatientId", "fDesc", "fInternalParasite",
                                                          "fExternalParasite", "fNextDate"})})
    bundle["appointments"] = []
    for r in t.get("tblAppointments"):
        if r.get("fId") is None:
            continue
        bundle["appointments"].append({
            "sourceId": str(r["fId"]), "petSourceId": _sid(r["fxPatientId"]) if r["fxPatientId"] in pets else None,
            "patientName": r["fPatientName"],
            "doctorSourceId": _resolve(ctx, "tblAppointments.fxDoctorId", r["fxDoctorId"], staff_ids, r["fId"]),
            "startUtc": to_utc(r["fDate"], r["fTime"]), "notes": r["fDesc"], "mobile": normalize_phone(r["fMobile"]),
            "isCheckedIn": bool(r["fIsCome"]),
            "legacy": _legacy(ctx, "tblAppointments", r, {"fId", "fxPatientId", "fPatientName", "fxDoctorId", "fDate",
                                                          "fTime", "fDesc", "fMobile", "fIsCome"})})
    bundle["holidays"] = [
        {"sourceId": str(r["fId"]), "title": _join(r["fName"], r["fDesc"]), "dateUtc": to_utc(r["fDate"]),
         "legacy": _legacy(ctx, "tblHolyDays", r, {"fId", "fDate", "fName", "fDesc"})}
        for r in t.get("tblHolyDays") if r.get("fId") is not None]
    _map_lab(t, ctx, bundle, pets)


def _map_lab(t: LegacyTables, ctx: _Ctx, bundle: dict[str, Any], pets: dict[int, dict[str, Any]]) -> None:
    out: list[dict[str, Any]] = []

    def add(table: str, r: dict[str, Any], kind: str, title: str | None, performed: Any, nxt: Any,
            values: dict[str, Any], notes: str | None, used: set[str]) -> None:
        pid = r.get("fxPatientId")
        if r.get("fId") is None or pid not in pets:
            ctx.warn(f"{table}: row dropped, patient not found", r.get("fId"))
            return
        out.append({"sourceTable": table, "sourceId": str(r["fId"]), "petSourceId": str(pid), "kind": kind,
                    "title": title, "performedUtc": to_utc(performed), "nextVisitUtc": to_utc(nxt),
                    "values": values, "notes": notes, "legacy": _legacy(ctx, table, r, used | {"fId", "fxPatientId"})})

    for r in t.get("tblBloodTestResult"):
        values = {c[1:]: r[c] for c in BLOOD_ANALYTES if r.get(c) is not None}
        kind = {1: "hematology", 2: "biochemistry"}.get(r["fTestType"], "other")
        notes_cols = HEMA_NOTES if kind == "hematology" else BIO_NOTES if kind == "biochemistry" else HEMA_NOTES + BIO_NOTES
        add("tblBloodTestResult", r, kind, None, r["fDate"], None, values, _join(*(r[c] for c in notes_cols)),
            set(BLOOD_ANALYTES) | {"fDate", "fTestType"} | set(notes_cols))
    urine_cols = ("fWeight", "fPh", "fGlucose", "fCatonBadi", "fNitrite", "fBillyRubin", "fAvrvbylynvzhn",
                  "fHemoglobin", "fHPFWhite", "fHPFRed", "fCrystal", "fProtein")
    for r in t.get("tblUrine"):
        add("tblUrine", r, "urine", "آزمایش ادرار", r["fDate"], r["fNextVisitDate"],
            {c[1:]: r[c] for c in urine_cols if r.get(c) is not None}, None, set(urine_cols) | {"fDate", "fNextVisitDate"})
    for r in t.get("tblEtcTest"):
        add("tblEtcTest", r, "other", r["fTestName"], r["fTestDate"], r["fNextVisite"],
            {"result": r["fResult"]} if r["fResult"] else {}, None, {"fTestName", "fTestDate", "fNextVisite", "fResult"})
    for r in t.get("tblRadiology"):
        add("tblRadiology", r, "radiology", r["fName"], r["fDate"], None, {}, _join(r["fDesc"], r["fDescription"]),
            {"fName", "fDate", "fDesc", "fDescription"})
    bundle["labResults"] = out


# =================================================================== finance
def _map_finance(t: LegacyTables, ctx: _Ctx, bundle: dict[str, Any], pets: dict[int, dict[str, Any]]) -> None:
    services = {int(s["sourceId"]) for s in bundle["services"]}
    products = {int(p["sourceId"]) for p in bundle["products"]}
    staff_ids = {int(s["sourceId"]) for s in bundle["staff"]}
    insurers = {int(i["sourceId"]) for i in bundle["insurers"]}
    ptypes = {int(p["sourceId"]) for p in bundle["paymentTypes"]}
    lines = t.group("tblChargeDetails", "fxCharge")
    pays = t.group("tblChargePay", "fxCharge")
    bundle["invoices"] = []
    for r in t.get("tblCharge"):
        cid = r.get("fId")
        if cid is None:
            continue
        inv_lines = []
        for d in lines.get(cid, []):
            kind = "service" if d["fxServices"] else "product" if d["fxStuff"] else "other"
            qty = d["fQuantity"] if d["fQuantity"] else 1
            unit = money_minor(d["fPrice"])
            total = money_minor(d["fTotalCharge"]) or money_minor(unit * qty)
            inv_lines.append({
                "sourceId": str(d["fId"]), "kind": kind,
                "serviceSourceId": _resolve(ctx, "tblChargeDetails.fxServices", d["fxServices"], services, d["fId"]),
                "productSourceId": _resolve(ctx, "tblChargeDetails.fxStuff", d["fxStuff"], products, d["fId"]),
                "staffSourceId": _resolve(ctx, "tblChargeDetails.fxStaff", d["fxStaff"], staff_ids, d["fId"]),
                "unitPriceMinor": unit, "quantity": qty, "totalMinor": total,
                "discountMinor": money_minor(d["fDiscountPrice"]), "description": None,
                "legacy": _legacy(ctx, "tblChargeDetails", d, {"fId", "fxCharge", "fxServices", "fxStuff", "fxStaff",
                                                               "fPrice", "fQuantity", "fTotalCharge", "fDiscountPrice"})})
        payments = [{
            "sourceId": str(p["fId"]), "amountMinor": money_minor(p["fPrice"]),
            "paymentTypeSourceId": _resolve(ctx, "tblChargePay.fxPaymentType", p["fxPaymentType"], ptypes, p["fId"]),
            "paidUtc": to_utc(p["fDate"]) or to_utc(r["fDate"], r["fTime"]),
            "legacy": _legacy(ctx, "tblChargePay", p, {"fId", "fxCharge", "fPrice", "fDate", "fxPaymentType"})}
            for p in pays.get(cid, []) if p.get("fId") is not None]
        paid = money_minor(r["fPay"])
        if not payments and paid > 0:  # header-only payment: derived, flagged by the "hdr:" id
            payments.append({"sourceId": f"hdr:{cid}", "amountMinor": paid,
                             "paymentTypeSourceId": _resolve(ctx, "tblCharge.fxPaymentType", r["fxPaymentType"], ptypes, cid),
                             "paidUtc": to_utc(r["fDate"], r["fTime"]), "legacy": None})
        total = money_minor(r["fTotalCharge"]) or sum(l["totalMinor"] for l in inv_lines)
        bundle["invoices"].append({
            "sourceId": str(cid), "petSourceId": _sid(r["fxPatient"]) if r["fxPatient"] in pets else None,
            "customerName": r["fNameFamily"], "mobile": normalize_phone(r["fMobileNo"]),
            "issuedUtc": to_utc(r["fDate"], r["fTime"]), "number": r["fFactorNo"] or r["fReceiptNo"],
            "totalMinor": total, "discountMinor": money_minor(r["fDiscount"]),
            "insurancePayMinor": money_minor(r["fInsurancePay"]),
            "insurerSourceId": _resolve(ctx, "tblCharge.fxInsurance", r["fxInsurance"], insurers, cid),
            "insuranceNo": r["fInsuranceNo"], "taxMinor": 0 if r["fIsNoTax"] else money_minor(r["fTax"]),
            "paidMinor": paid, "paymentTypeSourceId": _resolve(ctx, "tblCharge.fxPaymentType", r["fxPaymentType"], ptypes, cid),
            "isDebtor": bool(r["fIsDeptor"]), "notes": _join(r["fDesc"], r["fDiscountDetail"]),
            "lines": inv_lines, "payments": payments,
            "legacy": _legacy(ctx, "tblCharge", r, {"fId", "fDate", "fTime", "fxPatient", "fTotalCharge", "fInsurancePay",
                                                    "fDiscount", "fDiscountDetail", "fPay", "fxPaymentType", "fxInsurance",
                                                    "fInsuranceNo", "fIsDeptor", "fDesc", "fNameFamily", "fMobileNo",
                                                    "fReceiptNo", "fTax", "fIsNoTax", "fFactorNo"})})
    _map_stock(t, ctx, bundle, products)


def _map_stock(t: LegacyTables, ctx: _Ctx, bundle: dict[str, Any], products: set[int]) -> None:
    heads = {r["fId"]: r for r in t.get("tblStockHead") if r.get("fId") is not None}
    by_head: dict[Any, list[dict[str, Any]]] = collections.defaultdict(list)
    for s in t.get("tblStock"):
        if s.get("fId") is None:
            continue
        if s["fxStuff"] not in products:
            ctx.warn("tblStock: row dropped, product not found", s["fId"])
            continue
        by_head[s["fxStockHeadId"] if s["fxStockHeadId"] in heads else None].append(s)

    def line(s: dict[str, Any]) -> dict[str, Any]:
        return {"sourceId": str(s["fId"]), "productSourceId": str(s["fxStuff"]), "quantity": s["fQuantity"] or 0,
                "buyPriceMinor": money_minor(s["fBuyPrice"]), "expireUtc": to_utc(s["fExpireDate"]),
                "legacy": _legacy(ctx, "tblStock", s, {"fId", "fxStuff", "fQuantity", "fBuyPrice", "fExpireDate",
                                                       "fxStockHeadId"})}

    receipts = []
    for hid, h in heads.items():
        ls = [line(s) for s in by_head.get(hid, [])]
        receipts.append({
            "sourceId": str(hid), "number": h["fFactorNo"], "receivedUtc": to_utc(h["fFactorDate"]), "seller": h["fSeller"],
            "notes": h["fDesc"],
            "totalMinor": money_minor(h["fTotalPrice"]) or sum(money_minor(l["buyPriceMinor"] * l["quantity"]) for l in ls),
            "lines": ls,
            "legacy": _legacy(ctx, "tblStockHead", h, {"fId", "fFactorNo", "fFactorDate", "fSeller", "fDesc", "fTotalPrice"})})
    orphans = by_head.get(None)
    if orphans:
        ls = [line(s) for s in orphans]
        receipts.append({"sourceId": "orphan", "number": None, "receivedUtc": None, "seller": None,
                         "notes": "Stock rows without a receipt header",
                         "totalMinor": sum(money_minor(l["buyPriceMinor"] * l["quantity"]) for l in ls),
                         "lines": ls, "legacy": None})
    bundle["stockReceipts"] = receipts


# ===================================================================== entry
def build_bundle(t: LegacyTables, source_sha256: str, row_counts: dict[str, int],
                 include_raw: bool = False, extra_warnings: list[str] | None = None) -> dict[str, Any]:
    """Map every legacy table into the format-v2 bundle (deterministic apart from ``generatedUtc``)."""
    ctx = _Ctx()
    bundle: dict[str, Any] = {
        "formatVersion": BUNDLE_FORMAT_VERSION,
        "generatedUtc": _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "converterVersion": __version__, "sourceFingerprint": source_sha256,
        "sourceRowCounts": dict(sorted(row_counts.items())), "dedupeRule": DEDUPE_RULE,
        "currency": "IRT", "notes": list(STATIC_NOTES)}
    _map_lookups(t, ctx, bundle)
    _map_catalog(t, ctx, bundle)
    pets = _map_patients(t, ctx, bundle)
    _map_clinical(t, ctx, bundle, pets)
    _map_finance(t, ctx, bundle, pets)
    bundle["archive"] = {n: t.get(n) for n in ARCHIVE_TABLES if t.has(n) and t.get(n)}
    if include_raw:
        bundle["raw"] = {n: t.get(n) for n in sorted(row_counts) if t.has(n)}
    bundle["warnings"] = [*(extra_warnings or []), *t.warnings, *ctx.render()]
    return bundle
