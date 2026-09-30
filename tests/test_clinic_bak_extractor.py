"""Focused read-only extraction checks: python3 tests/test_clinic_bak_extractor.py."""
import copy
import hashlib
import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('clinic_extractor', ROOT / 'scripts/extract-clinic-bak.py')
EXTRACT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXTRACT)


class ClinicBackupTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = (ROOT / 'db-sample/Clinic.bak').read_bytes()
        cls.groups = EXTRACT.read_pages(cls.data)
        cls.schema = EXTRACT.catalog(cls.groups)
        cls.tables = {t['name']: [EXTRACT.decode_row(r, t['columns'])
                                 for r in cls.groups.get(t['allocation'], [])]
                      for t in cls.schema.values()}
        cls.preview = EXTRACT.map_preview(cls.tables)

    def test_exact_snapshot_and_all_table_counts(self):
        self.assertEqual(EXTRACT.SOURCE_SHA256, hashlib.sha256(self.data).hexdigest())
        self.assertEqual(70, len(self.schema))
        for table in self.schema.values():
            difference = table['expectedRows'] - len(self.tables[table['name']])
            self.assertEqual(1 if table['name'] in ('tblDays', 'tblJob') else 0, difference)
        self.assertEqual(694, len(self.tables['tblPatient']))

    def test_physical_layout_not_guessed_from_logical_column_order(self):
        table = next(t for t in self.schema.values() if t['name'] == 'tblPatient')
        identity = next(c for c in table['columns'] if c['name'] == 'fIdentity')
        self.assertEqual(1, identity['physicalOrdinal'])
        self.assertEqual(4, identity['offset'])
        first = self.tables['tblPatient'][0]
        self.assertEqual('1401/07/01', first['fAnimalBirthDate'])
        self.assertEqual('1402/12/14', first['fDateCreate'])
        self.assertEqual(17, first['fAnimalType'])

    def test_real_species_and_complete_stable_mapping(self):
        report = self.preview['report']
        self.assertEqual(547, report['owners'])
        self.assertEqual(694, report['pets'])
        self.assertEqual(9646, report['historyEntries'])
        pets = [p for o in self.preview['owners'] for p in o['pets']]
        self.assertEqual(638, sum(p['species'] == 0 for p in pets))
        self.assertEqual(56, sum(p['species'] == 1 for p in pets))
        self.assertEqual(self.preview, EXTRACT.map_preview(self.tables))
        self.assertFalse(any('weight' in p or 'gender' in p for p in pets))

    def test_shared_phone_different_names_are_not_merged(self):
        tables = copy.deepcopy(self.tables)
        tables['tblPatient'][1]['fMobileNo'] = tables['tblPatient'][0]['fMobileNo']
        mapped = EXTRACT.map_preview(tables)
        codes = ('1034', '1035')
        owners = [{p['code'] for p in o['pets']} for o in mapped['owners']]
        self.assertFalse(any(all(c in owner for c in codes) for owner in owners))

    def test_preserves_phones_and_unknowns_without_fake_data(self):
        pets = {p['code']: p for o in self.preview['owners'] for p in o['pets']}
        for row in self.tables['tblPatient']:
            mapped = pets[row['fId']]
            self.assertEqual(EXTRACT.normalize(row['fAnimalBirthDate']), mapped['birthDate'])
            self.assertEqual(EXTRACT.normalize(row['fDateCreate']), mapped['registeredDate'])
        self.assertTrue(any(not o['phone'] for o in self.preview['owners']))
        self.assertEqual('09120000000', EXTRACT.phone('۰۹۱۲-۰۰۰۰۰۰۰'))
        self.assertEqual('یکی', EXTRACT.normalize(' يكي '))

    def test_charge_details_remain_nonposting_provenance(self):
        entries = [h for o in self.preview['owners'] for p in o['pets'] for h in p['history']
                   if h['sourceTable'] == 'tblCharge']
        self.assertGreater(len(entries), 0)
        self.assertIn('UNVERIFIED', entries[0]['detail'])
        self.assertIn('legacyLines', entries[0]['detail'])
        self.assertNotIn('invoices', self.preview)


if __name__ == '__main__':
    unittest.main(verbosity=2)
