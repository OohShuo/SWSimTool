import unittest
import mujoco
import test_convert
from sites import normalize_site_references

class SiteIdentityTests(unittest.TestCase):
    setUp = test_convert.ConversionTests.setUp
    tearDown = test_convert.ConversionTests.tearDown
    run_convert = test_convert.ConversionTests.run_convert

    def identities(self):
        for i, site in enumerate(self.config['attachments']):
            site['id'] = 'site_' + str(i)
        normalize_site_references(self.config)

    def test_renames_resolve_sensor_and_both_constraint_endpoints(self):
        self.identities()
        for site in self.config['attachments']:
            site['name'] += '_renamed'
        model = self.run_convert()
        self.assertEqual(model.neq, 1)
        self.assertEqual(model.nsensor, 3)
        for site in self.config['attachments']:
            self.assertGreaterEqual(mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_SITE, site['name']), 0)
        self.assertEqual(self.config['sensors'][0]['site_id'], 'site_2')

    def test_deleted_identity_cannot_rebind_to_reused_name(self):
        self.identities()
        self.config['attachments'][2]['id'] = 'replacement'
        self.output.write_text('previous output')
        with self.assertRaisesRegex(ValueError, 'deleted or is missing'):
            self.run_convert()
        self.assertEqual(self.output.read_text(), 'previous output')

    def test_duplicate_identity_rejected(self):
        self.identities()
        self.config['attachments'][1]['id'] = 'site_0'
        with self.assertRaisesRegex(ValueError, 'Duplicate site identity'):
            self.run_convert()

    def test_legacy_names_migrate_without_changing_names(self):
        self.identities()
        self.assertEqual(self.config['equalities'][0]['site1_id'], 'site_0')
        self.assertEqual(self.config['equalities'][0]['site2_id'], 'site_1')
        self.assertEqual(self.config['sensors'][0]['site'], 'imu_mount')
