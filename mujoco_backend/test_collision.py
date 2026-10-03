import copy
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import mujoco
import numpy as np
from convert import convert, validate
from test_convert import URDF


class CollisionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.folder = Path(self.temp.name)
        self.urdf = self.folder / 'robot.urdf'
        self.urdf.write_text(URDF)
        self.sidecar = self.folder / 'robot.sim.json'
        self.output = self.folder / 'robot.xml'
        self.config = dict(schema_version=1, units='m,rad', urdf='robot.urdf', attachments=[],
                           collision=dict(disable_internal=True, link_modes={}, geometries=[], allowed_pairs=[]))
        self.links = [l.get('name') for l in ET.fromstring(URDF).findall('link')]

    def tearDown(self):
        self.temp.cleanup()

    def run_model(self):
        self.sidecar.write_text(json.dumps(self.config))
        return convert(self.urdf, self.sidecar, self.output)

    def geom(self, name, link, kind, size):
        return dict(name=name, link=link, type=kind, size=size, xyz=[0, 0, 0], rpy=[0, 0, 0])

    def test_legacy_sidecar_and_explicit_inertia_unchanged(self):
        self.config.pop('collision')
        old = self.run_model()
        self.config['collision'] = dict(link_modes={'arm': 'primitive'}, geometries=[self.geom('proxy', 'arm', 'box', [.1, .2, .3])])
        new = self.run_model()
        for link in self.links:
            np.testing.assert_array_equal(new.body(link).mass, old.body(link).mass)
            np.testing.assert_array_equal(new.body(link).inertia, old.body(link).inertia)
            np.testing.assert_array_equal(new.body(link).ipos, old.body(link).ipos)

    def test_all_primitives_dimensions_and_visual_mesh_disabled(self):
        rules = self.config['collision']
        rules['link_modes']['arm'] = 'primitive'
        for kind, size in [('box', [.2, .4, .6]), ('sphere', [.02]), ('cylinder', [.03, .1]), ('capsule', [.04, .2])]:
            with self.subTest(kind=kind):
                rules['geometries'] = [self.geom('proxy', 'arm', kind, size)]
                model = self.run_model()
                expected = [v/2 for v in size] if kind == 'box' else size if kind == 'sphere' else [size[0], size[1]/2]
                np.testing.assert_allclose(model.geom('proxy').size[:len(expected)], expected)
                self.assertEqual(model.geom('proxy').group, 3)
                xml = ET.parse(self.output)
                others = [g for g in xml.findall(".//body[@name='arm']/geom") if g.get('name') != 'proxy']
                self.assertTrue(others)
                self.assertTrue(all(g.get('contype') == '0' and g.get('conaffinity') == '0' for g in others))

    def test_default_excludes_and_parent_child_allow_pair(self):
        rules = self.config['collision']
        for link in self.links:
            rules['link_modes'][link] = 'primitive'
            rules['geometries'].append(self.geom('proxy_' + link, link, 'sphere', [1]))
        disabled = self.run_model()
        data = mujoco.MjData(disabled)
        mujoco.mj_forward(disabled, data)
        self.assertEqual(data.ncon, 0)
        self.assertEqual(disabled.nexclude, len(self.links)*(len(self.links)-1)//2)
        rules['allowed_pairs'] = [dict(link1=self.links[0], link2=self.links[1])]
        allowed = self.run_model()
        data = mujoco.MjData(allowed)
        mujoco.mj_forward(allowed, data)
        self.assertGreater(data.ncon, 0)
        self.assertEqual(allowed.nexclude, len(self.links)*(len(self.links)-1)//2-1)
        self.assertEqual(allowed.npair, 1)

    def test_multiple_proxies_expand_pairs(self):
        rules = self.config['collision']
        for link in self.links:
            rules['link_modes'][link] = 'primitive'
            for i in range(2):
                rules['geometries'].append(self.geom(f'{link}_{i}', link, 'sphere', [.01]))
        rules['allowed_pairs'] = [dict(link1=self.links[0], link2=self.links[1])]
        self.assertEqual(self.run_model().npair, 4)

    def test_none_mode_and_world_contacts_are_separate(self):
        rules = self.config['collision']
        rules['link_modes'][self.links[0]] = 'none'
        rules['link_modes']['arm'] = 'primitive'
        rules['geometries'] = [self.geom('proxy', 'arm', 'sphere', [1])]
        self.run_model()
        xml = ET.parse(self.output).getroot()
        ET.SubElement(xml.find('worldbody'), 'geom', name='floor', type='plane', size='5 5 .1')
        model = mujoco.MjModel.from_xml_string(ET.tostring(xml, encoding='unicode'))
        data = mujoco.MjData(model)
        mujoco.mj_forward(model, data)
        self.assertGreater(data.ncon, 0)

    def test_invalid_configs_rejected_and_output_preserved(self):
        self.run_model()
        original = self.output.read_bytes()
        invalid = [dict(link_modes={'missing': 'mesh'}), dict(link_modes={'arm': 'primitive'}),
                   dict(allowed_pairs=[dict(link1='arm', link2='arm')]),
                   dict(geometries=[self.geom('bad', 'arm', 'box', [1, 0, 2])]),
                   dict(link_modes={'arm': 'none'}, allowed_pairs=[dict(link1=self.links[0], link2='arm')])]
        for rules in invalid:
            self.config['collision'] = rules
            with self.assertRaises(ValueError):
                self.run_model()
            self.assertEqual(original, self.output.read_bytes())

    def test_unknown_link_and_duplicate_proxy(self):
        rules = self.config['collision']
        rules['geometries'] = [self.geom('bad', 'missing', 'sphere', [.1])]
        with self.assertRaises(ValueError):
            validate(self.config, ET.fromstring(URDF))
        rules['geometries'] = [self.geom('same', 'arm', 'sphere', [.1])] * 2
        with self.assertRaises(ValueError):
            validate(self.config, ET.fromstring(URDF))


if __name__ == '__main__':
    unittest.main()
