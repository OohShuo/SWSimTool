"""Packaging boundaries, independent of CAD or a local Python tool environment."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET
ROOT = next(p for p in Path(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
class LayoutTests(unittest.TestCase):
 def test_production_source_and_dependency_boundary(self):
  project = ET.parse(ROOT / 'src/SWSimTool.SolidWorks/SWSimTool.SolidWorks.csproj')
  ns = {'m': 'http://schemas.microsoft.com/developer/msbuild/2003'}
  sources = [x.attrib['Include'].replace('\\','/') for x in project.findall('.//Compile', ns)]
  self.assertFalse(any(x.startswith('Test/') or '/tests/' in x for x in sources))
  references = [x.attrib['Include'].lower() for x in project.findall('.//Reference',ns)]
  self.assertFalse(any(any(t in x for t in ['xunit','moq','castle','testplatform','testframework']) for x in references))
  content = [x.attrib['Include'].replace('\\','/') for x in project.findall('.//Content',ns)]
  python = [x for x in content if x.endswith('.py')]
  self.assertEqual({Path(x).name for x in python}, {'native_support.py','simplify_stl.py','mesh_cache.py','incremental.py','solver.py'})
  self.assertTrue(all('runtime/python/' in x for x in python))
 def test_release_payload(self):
  payload = ROOT / 'build/runtime-release'
  self.assertEqual({p.name.lower() for p in payload.glob('*.dll')}, {'swsimtool.dll','swsimtool.core.dll','swsimtool.application.dll','swsimtool.infrastructure.dll','csvhelper.dll','mathnet.numerics.dll','log4net.dll','solidworkstools.dll'})
  self.assertEqual({p.name for p in (payload/'mujoco_backend').glob('*.py')}, {'native_support.py','simplify_stl.py','mesh_cache.py','incremental.py','solver.py'})
 def test_source_roots_and_test_split(self):
  self.assertFalse((ROOT/'scripts').exists())
  self.assertFalse((ROOT/'TestRunner').exists())
  self.assertFalse((ROOT/'SWSimTool/Test').exists())
  self.assertTrue((ROOT/'tests/upstream/SWSimTool.Tests.csproj').is_file())
  self.assertTrue((ROOT/'tools/docs/Build-Guide.py').is_file())
if __name__ == '__main__': unittest.main(verbosity=2)
