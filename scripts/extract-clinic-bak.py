#!/usr/bin/env python3
"""Read-only, snapshot-specific SQL Server page reader (stdlib only).

Not a general RESTORE replacement: compressed/encrypted/LOB/row-overflow data
must be restored by SQL Server. Reject a different backup rather than guessing.
Outputs contain personal data: keep them local, not in source control.
"""
import argparse
import collections
import hashlib
import json
import re
import struct
from pathlib import Path

PAGE = 8192
BASE = 7680
SOURCE_SHA256 = 'ae4affdd3a5d4c116ed8e55a7f10227970d4af029a7ab7d77b0262149b733414'


def u16(b, p=0):
    return struct.unpack_from('<H', b, p)[0]


def u32(b, p=0):
    return struct.unpack_from('<I', b, p)[0]


def read_pages(data):
    groups = collections.defaultdict(list)
    for off in range(BASE, len(data) - PAGE + 1, PAGE):
        page = data[off:off + PAGE]
        if page[:2] != b'\x01\x01' or u16(page, 36) != 1:
            continue
        slots = u16(page, 22)
        if not 0 < slots < 1000:
            raise ValueError('Invalid page slot count')
        locations = [u16(page, PAGE - 2 * (i + 1)) for i in range(slots)]
        # Logical slots need not follow physical record order. Read the record's
        # own fixed/variable lengths, never use the next logical slot as its end.
        end = PAGE - 2 * slots
        for start in locations:
            if not 96 <= start < end <= PAGE - 2 * slots:
                raise ValueError('Invalid record bounds')
            rec = page[start:end]
            # Primary data records only; ghosts/deleted rows are not live data.
            if rec[0] & 14 or not rec[0] & 16:
                continue
            fixed_end = u16(rec, 2)
            if not 4 <= fixed_end <= len(rec) - 2:
                raise ValueError('Unsupported record header')
            count = u16(rec, fixed_end)
            nulls = rec[fixed_end + 2:fixed_end + 2 + (count + 7) // 8]
            pos = fixed_end + 2 + len(nulls)
            values = []
            if rec[0] & 32:
                nvar = u16(rec, pos)
                pos += 2
                ends = [u16(rec, pos + 2 * i) for i in range(nvar)]
                pos += 2 * nvar
                for encoded_stop in ends:
                    stop = encoded_stop & 32767
                    if not pos <= stop <= len(rec):
                        raise ValueError('Invalid variable-column bounds')
                    raw = rec[pos:stop]
                    values.append({'complexHex': raw.hex()} if encoded_stop & 32768 else raw)
                    pos = stop
            groups[u32(page, 24)].append((rec[4:fixed_end], values, count, nulls))
    return groups


def catalog(groups):
    tables = {}
    for fixed, values, _, _ in groups[34]:  # sys.sysschobjs
        if values:
            name = values[0].decode('utf-16-le')
            if name.startswith('tbl'):
                tables[u32(fixed)] = {'name': name, 'columns': []}
    for fixed, values, _, _ in groups[41]:  # sys.syscolpars
        table = tables.get(u32(fixed))
        if table and values and u16(fixed, 4) == 0:
            table['columns'].append({'ordinal': u32(fixed, 6),
                                     'name': values[0].decode('utf-16-le'),
                                     'type': fixed[10], 'length': u16(fixed, 15)})
    # Follow sysrowsets -> sysallocunits -> sysrscols instead of guessing table
    # identities or physical order (tblPatient was altered; fIdentity is first).
    allocations = {}
    for fixed, _, _, _ in groups[7]:
        if fixed[8] == 1:  # IN_ROW_DATA
            allocations[fixed[9:17]] = int.from_bytes(fixed[:8], 'little') >> 16 & 0xffffffff
    layouts = collections.defaultdict(dict)
    for fixed, _, _, _ in groups[3]:
        layouts[fixed[:8]][u32(fixed, 8)] = {
            'physicalOrdinal': u32(fixed, 12),
            'offset': struct.unpack_from('<h', fixed, 40)[0],
            'bit': fixed[48]}
    for fixed, _, _, _ in groups[5]:
        table = tables.get(u32(fixed, 9))
        if table is not None and u32(fixed, 13) in (0, 1):
            rid = fixed[:8]
            table['allocation'] = allocations.get(rid)
            table['expectedRows'] = int.from_bytes(fixed[27:35], 'little')
            for col in table['columns']:
                col.update(layouts[rid][col['ordinal']])
    for table in tables.values():
        table['columns'].sort(key=lambda c: c['ordinal'])
    return tables


def decode_row(row, columns):
    fixed, variables, count, nulls = row
    # Physical ordinals include dropped columns, unlike the logical schema.
    if count > 128:
        raise ValueError('Schema/row column count mismatch')
    out = {}
    for col in columns:
        typ, length = col['type'], col['length']
        index = col['physicalOrdinal'] - 1
        # ALTER TABLE can append nullable columns without rewriting old rows.
        is_null = index >= count or bool(nulls[index // 8] & (1 << (index % 8)))
        offset = col['offset']
        if is_null:
            out[col['name']] = None
            continue
        if offset < 0:
            var = -offset - 1
            raw = variables[var] if var < len(variables) else b''
            if isinstance(raw, dict):
                out[col['name']] = raw
                continue
        elif typ == 104:
            raw = bytes([(fixed[offset - 4] >> col['bit']) & 1])
        else:
            raw = fixed[offset - 4:offset - 4 + length]
            if len(raw) != length:
                raise ValueError('Fixed column bounds mismatch')
        if is_null:
            value = None
        elif typ in (231, 239):
            value = raw.decode('utf-16-le').rstrip()
        elif typ in (167, 175):
            value = raw.decode('cp1256').rstrip()
        elif typ in (48, 52, 56, 127):
            value = int.from_bytes(raw, 'little', signed=typ != 48)
        elif typ == 104:
            value = bool(raw[0])
        elif typ == 62:
            value = struct.unpack('<d', raw)[0]
        elif typ == 59:
            value = struct.unpack('<f', raw)[0]
        else:
            value = {'sqlType': typ, 'hex': raw.hex()}
        out[col['name']] = value
    return out


def normalize(value):
    return re.sub(r'\s+', ' ', str(value or '').replace('ي', 'ی').replace('ك', 'ک')).strip()


def phone(value):
    return ''.join(str(int(c)) for c in normalize(value) if c.isdecimal())


def map_preview(tables):
    lookups = {name: {str(r['fId']): normalize(r.get('fName')) for r in tables[name]}
               for name in ('tblAnimalType', 'tblBreed', 'tblColors')}
    species = {'سگ': 0, 'گربه': 1, 'پرنده': 2, 'جونده': 3, 'خزنده': 4}
    owners = {}
    patients = {}
    excluded = []
    for row in tables['tblPatient']:
        code = normalize(row['fId'])
        name = normalize(f"{row['fFirstName'] or ''} {row['fLastName'] or ''}")
        if not name or not normalize(row['fAnimalName']):
            excluded.append({'table': 'tblPatient', 'id': code, 'reason': 'missing owner or pet name'})
            continue
        if code in patients:
            raise ValueError('Duplicate live patient code')
        primary = phone(row['fMobileNo']) or phone(row['fTelNo'])
        # A shared phone is not an owner identity: require name + contact + national ID.
        key = (name, primary, normalize(row['fNationalCode'])) if primary else (name, code)
        owner = owners.setdefault(key, {'fullName': name, 'phone': primary,
            'secondaryPhone': phone(row['fTelNo']) if phone(row['fTelNo']) != primary else '',
            'address': normalize(row['fAddress']), 'city': normalize(row['fCity']),
            'email': normalize(row['fEmail']), 'nationalId': normalize(row['fNationalCode']),
            'registeredDate': normalize(row['fDateCreate']), 'pets': []})
        for field, src in (('address', 'fAddress'), ('city', 'fCity'), ('email', 'fEmail')):
            if not owner[field]:
                owner[field] = normalize(row[src])
        registered = normalize(row['fDateCreate'])
        if registered and (not owner['registeredDate'] or registered < owner['registeredDate']):
            owner['registeredDate'] = registered
        pet = {'code': code, 'name': normalize(row['fAnimalName']),
               'species': species.get(lookups['tblAnimalType'].get(str(row['fAnimalType'])), 5),
               'breed': lookups['tblBreed'].get(str(row['fAnimalBreed'])) if row['fAnimalBreed'] else None,
               'color': lookups['tblColors'].get(str(row['fAnimalColor'])) if row['fAnimalColor'] else None,
               'birthDate': normalize(row['fAnimalBirthDate']), 'registeredDate': registered,
               'isNeutered': bool(row['fAnimalIsSterile']), 'isInactive': bool(row['fIsInActive']),
               # Legacy sex-code semantics are not documented; don't guess Male/Female.
               'microchip': normalize(row['fCodeMicrochip']), 'implantDate': normalize(row['fDateKasht']),
               'history': [{'sourceTable': 'tblPatient', 'sourceId': code,
                   'date': registered, 'title': 'پروندهٔ اصلی کلینیک قدیمی',
                   'detail': json.dumps(row, ensure_ascii=False, separators=(',', ':'))}]}
        owner['pets'].append(pet)
        patients[code] = pet
    titles = {'tblPatientRecord': 'معاینهٔ ثبت‌شده در کلینیک قدیمی',
              'tblVaccination': 'سابقهٔ واکسیناسیون کلینیک قدیمی (بدون ایجاد برنامهٔ جدید)',
              'tblAntiParasite': 'سابقهٔ ضدانگل کلینیک قدیمی',
              'tblAppointments': 'نوبت ثبت‌شده در کلینیک قدیمی',
              'tblCharge': 'صورتحساب کلینیک قدیمی (غیرقابل ثبت در دفتر مالی جدید)'}
    details = collections.defaultdict(list)
    for row in tables['tblChargeDetails']:
        details[str(row['fxCharge'])].append(row)
    for table, title in titles.items():
        patient_column = 'fxPatient' if table == 'tblCharge' else 'fxPatientId'
        for row in tables[table]:
            code = normalize(row[patient_column])
            pet = patients.get(code)
            if pet is None:
                excluded.append({'table': table, 'id': str(row['fId']), 'reason': 'patient not present in preview'})
                continue
            record = dict(row)
            if table == 'tblCharge':
                record['legacyLines'] = details[str(row['fId'])]
                record['currency'] = 'UNVERIFIED: original amounts, no currency conversion'
            pet['history'].append({'sourceTable': table, 'sourceId': str(row['fId']),
                'date': normalize(row['fDate']), 'title': title,
                'detail': json.dumps(record, ensure_ascii=False, separators=(',', ':'))})
    return {'formatVersion': 1, 'sourceSha256': SOURCE_SHA256, 'owners': list(owners.values()),
            'report': {'owners': len(owners), 'pets': len(patients),
                'historyEntries': sum(len(p['history']) for p in patients.values()),
                'excludedFromPreview': excluded,
                'archiveOnly': 'All 70 tables preserved separately. Inventory, financial postings, appointments and vaccine schedules are not fabricated. See archive schema and raw rows.',
                'unknownFields': 'Legacy sex codes and currency unverified; no guessed genders, weights, doses, staff identities, stock balances or payments.'}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('backup', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    data = args.backup.read_bytes()
    if hashlib.sha256(data).hexdigest() != SOURCE_SHA256:
        raise ValueError('Unsupported backup snapshot: use SQL Server RESTORE for a different file.')
    groups = read_pages(data)
    tables = catalog(groups)
    exported = {}
    for table in tables.values():
        if 'allocation' not in table:
            raise ValueError(f"Missing allocation metadata: {table['name']}")
        rows = [decode_row(row, table['columns']) for row in groups.get(table['allocation'], [])]
        # Two legacy lookup tables contain one deleted slot each; the old
        # sysrowsets cardinality still includes it. No other discrepancy is accepted.
        missing = table['expectedRows'] - len(rows)
        allowed = 1 if table['name'] in ('tblDays', 'tblJob') else 0
        if missing != allowed:
            raise ValueError(f"Row count mismatch: {table['name']} ({len(rows)} vs {table['expectedRows']})")
        table['deletedSlotsExcluded'] = missing
        table['extractedRows'] = len(rows)
        exported[table['name']] = rows
    rows = exported['tblPatient']
    result = {'sourceSha256': hashlib.sha256(data).hexdigest(),
              'schema': sorted(tables.values(), key=lambda t: t['name']),
              'tables': exported}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    archive = args.output.with_name(args.output.stem + '-archive.json')
    archive.write_text(json.dumps(result, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    preview = map_preview(exported)
    args.output.write_text(json.dumps(preview, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    report = args.output.with_name(args.output.stem + '-report.json')
    report.write_text(json.dumps({'sourceSha256': SOURCE_SHA256, 'schema': result['schema'],
                                 'mapping': preview['report']}, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f"Verified {len(tables)} table schemas; {len(rows)} source patient rows.")
    print(f"Preview: {preview['report']['owners']} owners, {preview['report']['pets']} pets, {preview['report']['historyEntries']} history entries.")
    print(f'Private outputs (do not commit): {args.output}, {archive}, {report}')


if __name__ == '__main__':
    main()
