"""Typed per-table extractors for the legacy Clinic database (plan §2.3).

``SPECS`` declares, for every legacy table the converter understands, the
expected columns and their *kind*. :class:`LegacyTables` reads rows through an
:class:`~converter.mdf_reader.MdfReader` and returns dict rows where

* every spec column is present (``None`` when the backup lacks it - the legacy
  schema drifted over the years, so extraction never fails on a missing column),
* values are coerced by kind (text normalised, ints/bools/money typed),
* any extra column found in the backup is kept untouched (feeds ``legacy``).

Dates stay raw Jalali strings here; conversion is :mod:`converter.convert`.
"""
from __future__ import annotations

import re
from typing import Any, Iterable, Protocol

# kinds: id int text bool money num date time blob
_K = {
    "i": "id", "n": "int", "t": "text", "b": "bool", "m": "money",
    "f": "num", "d": "date", "h": "time", "x": "blob",
}


def _spec(**cols: str) -> dict[str, str]:
    return {name: _K[kind] for name, kind in cols.items()}


def _lookup() -> dict[str, str]:
    return _spec(fId="i", fName="t")


_BLOOD_ANALYTES = (
    "fHet fHb fRBC fMCV fMCH fMCHC fNRBC fRetic fESR1h fWBC fDIFF fSeg fBand fLymph fMono fEos fBaso fPlt "
    "fTotalProtein fAlbumin fGlobulin fFibrinogen fGlocose fTriglycerides fCholesterol fHDLCholesterol "
    "fLDLCholesterol fBilirubinTotal fBilirobinDirect fBUN fUrea fCreatinine fAcidPhosphatase fASTGOT fALTGPT "
    "fGGT fCK fLDH fCa fP fMg fFe fTIBC fLipase fAmylase fUricAcid fALP fOtherCells fRDW fK fPt fHbAlc fNa fCl "
    "fPtt fAGRatio fBilirobinInDirect fScr fHct"
).split()

SPECS: dict[str, dict[str, str]] = {
    # lookups
    "tblAnimalType": _lookup(), "tblBreed": _lookup(), "tblColors": _lookup(),
    "tblIdentifier": _lookup(), "tblInsurances": _lookup(), "tblBaseInfo": _lookup(),
    "tblPaymentType": _lookup(), "tblFactorType": _lookup(), "tblStuffGroup": _lookup(),
    "tblVaccinationType": _lookup(), "tblDesignation": _lookup(), "tblSpecialty": _lookup(),
    "tblPetienRecordDetailType": _spec(fId="i", fType="n", fName="t"),
    "tblStaff": _spec(fId="i", fFirstName="t", fLastName="t", fxDesignation="i", fxSpecialty="i"),
    # patients
    "tblPatient": _spec(
        fId="i", fFirstName="t", fLastName="t", fAnimalName="t", fAnimalType="i", fAnimalSex="n",
        fAnimalIsSterile="b", fAnimalBirthDate="d", fAnimalAge="n", fAnimalAgeType="n", fAnimalColor="i",
        fAnimalBreed="i", fAnimalBloodClass="t", f1thVaccinationDate="d", f2thVaccinationDate="d",
        fRabiesVaccinationDate="d", fxDoctorId="i", fxInsuranceId="i", fInsuranceNo="t", fTelNo="t",
        fMobileNo="t", fEmail="t", fAddress="t", fCity="t", fxDiseaseId="i", fDesc="t", fImage="x",
        fDateCreate="d", fIdentity="t", fDescHistory="t", fxIdentifier="i", fxBehaviorId="i",
        fxTypeBuyId="i", fxPlaceId="i", fxFoodId="i", fHasOperation="b", fHasTest="b", fCodeMicrochip="t",
        fDateKasht="d", fSex="n", fDateWaiting="d", fTimeWaiting="h", fBarcode="t", fNationalCode="t",
        fIsInActive="b"),
    "tblEnglishInfo": _spec(
        fId="i", fxPatientId="i", fMicrochipNo="t", fIDNo="t", fAnimalName="t", fSpecies="t",
        fAnimalBreed="t", fAnimalBirthDate="d", fAnimalBirthPlace="t", fAnimalSex="t", fAnimalColor="t",
        fOwner="t", fCity="t", fOwnershipDate="d", fTel="t", fAddress="t", fMicrochipDate="d",
        fDescription="t"),
    # medical
    "tblPatientRecord": _spec(fId="i", fTime="h", fxPatientId="i", fxDoctorId="i", fDate="d",
                              fDiagnosis="t", fResults="t", fDateNext="d", fxReason="i"),
    "tblPaientRecordDetail": _spec(fId="i", fxPatientRecordId="i", fxTypeId="i", fName="t"),
    "tblVaccination": _spec(fId="i", fxPatientId="i", fxVaccinationTypeId="i", fName="t", fDesc="t",
                            fDate="d", fNextDate="d"),
    "tblAntiParasite": _spec(fId="i", fDate="d", fxPatientId="i", fDesc="t", fInternalParasite="t",
                             fExternalParasite="t", fNextDate="d"),
    "tblAppointments": _spec(fId="i", fxPatientId="i", fPatientName="t", fxDoctorId="i", fDate="d",
                             fTime="h", fDesc="t", fMobile="t", fDateReal="d", fIsCome="b",
                             fSMSTemplateSend="b"),
    "tblDays": _spec(fId="i", fName="t"),
    "tblStaffDaysStatus": _spec(fId="i", fxStaff="i", fxDay="i", fStart="h", fEnd="h"),
    "tblHolyDays": _spec(fId="i", fDate="d", fName="t", fDesc="t"),
    # sales
    "tblCharge": _spec(fId="i", fDate="d", fTime="h", fxPatient="i", fTotalCharge="m", fInsurancePay="m",
                       fDiscount="m", fDiscountDetail="t", fPay="m", fxPaymentType="i", fxInsurance="i",
                       fInsuranceNo="t", fCharge="m", fIsDeptor="b", fDesc="t", fxUser="i",
                       fNameFamily="t", fxFactorType="i", fMobileNo="t", fReceiptNo="t", fTax="m",
                       fIsNoTax="b", fFactorNo="t"),
    "tblChargeDetails": _spec(fId="i", fxCharge="i", fxServices="i", fxStuff="i", fxStaff="i", fPrice="m",
                              fQuantity="f", fTotalCharge="m", fCharge="m", fChargeMasrafi="m",
                              fInsurancePay="m", fDiscountPrice="m", fDiscountPercent="f", fTaxDetail="m"),
    "tblChargePay": _spec(fId="i", fxCharge="i", fPrice="m", fDate="d", fxPaymentType="i"),
    "tblServices": _spec(fId="i", fName="t", fPrice="m", fxStaffId="i"),
    # inventory
    "tblStuff": _spec(fId="i", fName="t", fPrice="m", fPriceBuy="m", fBarCode="t", fOrderPoint="f",
                      fxStuffGroup="i"),
    "tblStock": _spec(fId="i", fxStuff="i", fQuantity="f", fBuyPrice="m", fExpireDate="d", fDesc="t",
                      fxStockHeadId="i", fDate="d"),
    "tblStockHead": _spec(fId="i", fFactorNo="t", fFactorDate="d", fSeller="t", fTel="t", fDesc="t",
                          fType="n", fxFactorType="i", fSellerCode="t", fxPaymentType="i",
                          fTotalPrice="m", fTotalPay="m", fTotalDiscount="m", fTax="m"),
    "tblWare": _spec(fId="i", fWareNo="t", fWareDate="d", fType="n"),
    "tblWareDetail": _spec(fId="i", fxWare="i", fxStuff="i", fQuantity="f", fPrice="m", fStockCount="f",
                           fDate="d"),
    "tblConsumerStuffHead": _spec(fId="i", fxCharge="i", fxPatient="i", fDate="d"),
    "tblConsumerStuff": _spec(fId="i", fxConsumerStuffHead="i", fxStuff="i", fxStaff="i", fQuantity="f",
                              fBuyPrice="m", fPrice="m", fTotalPrice="m", fTime="h"),
    # lab
    "tblBloodTest": _spec(fId="i", fxAnimalTypeId="i"),
    "tblBloodTestResult": {
        **_spec(fId="i", fxPatientId="i", fDate="d", fTestType="n", fFile="x", fFileExt="t",
                fHemaNote="t", fHemaReport="t", fHemaInterpretation="t",
                fBioNote="t", fBioReport="t", fBioInterpretation="t"),
        **{c: "num" for c in _BLOOD_ANALYTES}},
    "tblUrine": _spec(fId="i", fxPatientId="i", fDate="d", fNextVisitDate="d", fWeight="f", fPh="f",
                      fGlucose="t", fCatonBadi="t", fNitrite="t", fBillyRubin="t", fAvrvbylynvzhn="t",
                      fHemoglobin="t", fHPFWhite="t", fHPFRed="t", fCrystal="t", fProtein="t"),
    "tblEtcTest": _spec(fId="i", fxPatientId="i", fTestName="t", fTestDate="d", fNextVisite="d", fResult="t"),
    "tblRadiology": _spec(fId="i", fxPatientId="i", fDate="d", fDesc="t", fDescription="t", fName="t"),
}

BLOOD_ANALYTES = tuple(_BLOOD_ANALYTES)
_WS = re.compile(r"\s+")


class _Source(Protocol):
    def has_table(self, name: str) -> bool: ...
    def rows(self, name: str) -> list[dict[str, Any]]: ...
    def row_counts(self) -> dict[str, int]: ...


def _text(value: Any) -> str | None:
    if value is None:
        return None
    if isinstance(value, dict):  # undecodable blob placeholder
        return None
    s = _WS.sub(" ", str(value).replace("ي", "ی").replace("ك", "ک").replace("\u200c\u200c", "\u200c")).strip()
    return s or None


def _int(value: Any) -> int | None:
    if value is None or isinstance(value, (dict, bool)):
        return int(value) if isinstance(value, bool) else None
    if isinstance(value, (int, float)):
        return int(value)
    s = _text(value)
    if s is None:
        return None
    try:
        return int(float(s))
    except ValueError:
        return None


def _num(value: Any) -> float | int | None:
    if value is None or isinstance(value, dict):
        return None
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, (int, float)):
        return value
    s = _text(value)
    if s is None:
        return None
    try:
        f = float(s)
    except ValueError:
        return None
    return int(f) if f.is_integer() else f


def _bool(value: Any) -> bool:
    if isinstance(value, (bool, int, float)):
        return bool(value)
    s = (_text(value) or "").lower()
    return s in ("1", "true", "yes", "y", "t")


def _money(value: Any) -> int:
    n = _num(value)
    return 0 if n is None else int(round(n))


def _coerce(kind: str, value: Any) -> Any:
    if kind == "id":
        n = _int(value)
        return None if n is None else n
    if kind == "int":
        return _int(value)
    if kind == "bool":
        return _bool(value)
    if kind == "money":
        return _money(value)
    if kind == "num":
        return _num(value)
    if kind == "blob":
        return None if value is None else (value if isinstance(value, dict) else {"text": _text(value)})
    return _text(value)  # text, date, time stay raw strings


class LegacyTables:
    """Typed, drift-tolerant access to every legacy table (cached)."""

    def __init__(self, source: _Source) -> None:
        self._source = source
        self._cache: dict[str, list[dict[str, Any]]] = {}
        self.warnings: list[str] = []

    def has(self, name: str) -> bool:
        return self._source.has_table(name)

    def get(self, name: str) -> list[dict[str, Any]]:
        """Rows of ``name`` (empty list + warning when the table is absent)."""
        cached = self._cache.get(name)
        if cached is not None:
            return cached
        if not self._source.has_table(name):
            self.warnings.append(f"table {name} not present in backup; treated as empty")
            self._cache[name] = []
            return self._cache[name]
        spec = SPECS.get(name, {})
        out: list[dict[str, Any]] = []
        for raw in self._source.rows(name):
            row: dict[str, Any] = {}
            for col, kind in spec.items():
                row[col] = _coerce(kind, raw.get(col))
            for col, value in raw.items():  # extras kept for lossless passthrough
                if col not in spec:
                    row[col] = value if isinstance(value, (int, float, bool)) or value is None else _text(value)
            out.append(row)
        self._cache[name] = out
        return out

    def by_id(self, name: str, key: str = "fId") -> dict[int, dict[str, Any]]:
        return {r[key]: r for r in self.get(name) if r.get(key) is not None}

    def lookup(self, name: str) -> dict[int, str]:
        """``fId -> fName`` for a simple lookup table."""
        return {r["fId"]: r["fName"] for r in self.get(name) if r.get("fId") is not None and r.get("fName")}

    def group(self, name: str, key: str) -> dict[int, list[dict[str, Any]]]:
        """Rows grouped by foreign key column ``key``."""
        out: dict[int, list[dict[str, Any]]] = {}
        for r in self.get(name):
            if r.get(key) is not None:
                out.setdefault(r[key], []).append(r)
        return out

    def counts(self, names: Iterable[str] | None = None) -> dict[str, int]:
        wanted = list(names) if names is not None else sorted(SPECS)
        return {n: len(self.get(n)) for n in wanted if self.has(n)}
