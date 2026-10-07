"""Source gates for the centralized SDK build, without CAD or network access."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET
ROOT = Path(__file__).resolve().parents[2]

class SdkBuild(unittest.TestCase):
    def test_projects_are_sdk_and_dependencies_are_restorable(self):
        projects = list((ROOT / 'src').rglob('*.csproj')) + list((ROOT / 'tests').rglob('*.csproj'))
        self.assertGreaterEqual(len(projects), 8)
        for path in projects:
            project = ET.parse(path).getroot()
            self.assertEqual(project.get('Sdk'), 'Microsoft.NET.Sdk', str(path))
            for hint in project.findall('.//HintPath'):
                self.assertNotIn('packages', hint.text.lower())
                self.assertNotIn('log4net.dll', hint.text.lower())
        self.assertEqual(list((ROOT / 'src').rglob('packages.config')), [])
        self.assertEqual(list((ROOT / 'tests').rglob('packages.config')), [])

    def test_central_version_and_compiler_paths(self):
        version = ET.parse(ROOT / 'Version.props').findtext('.//SWSimToolVersion')
        self.assertRegex(version, r'^\d+\.\d+\.\d+$')
        props = (ROOT / 'Directory.Build.props').read_text()
        self.assertIn('Version.props', props)
        self.assertIn('build/obj/$(MSBuildProjectName)/', props)
        self.assertIn('build/bin/$(MSBuildProjectName)/', props)
        self.assertIn('<Deterministic>true</Deterministic>', props)
        for source in (ROOT / 'src').rglob('AssemblyInfo.cs'):
            self.assertNotRegex(source.read_text(), r'Assembly(?:File|Informational)?Version\(')

if __name__ == '__main__': unittest.main(verbosity=2)
