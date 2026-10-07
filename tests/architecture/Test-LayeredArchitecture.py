"""Verify actual project dependencies and source responsibilities."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET
ROOT = Path(__file__).resolve().parents[2]
NS = {'m': 'http://schemas.microsoft.com/developer/msbuild/2003'}
class Architecture(unittest.TestCase):
    def project(self, layer):
        directory = ROOT / 'src' / ('SWSimTool.' + layer)
        return directory, ET.parse(directory / ('SWSimTool.' + layer + '.csproj'))
    def test_project_graph(self):
        expected = {'Core': set(), 'Application': {'Core'}, 'Infrastructure': {'Core', 'Application'}, 'SolidWorks': {'Core', 'Application', 'Infrastructure'}}
        for layer, wanted in expected.items():
            directory, xml = self.project(layer)
            actual = {Path(x.attrib['Include'].replace('\\', '/')).stem.removeprefix('SWSimTool.') for x in xml.findall('.//ProjectReference', NS)}
            self.assertEqual(actual, wanted, layer)
            if layer != 'SolidWorks':
                refs = [x.attrib['Include'] for x in xml.findall('.//Reference', NS)]
                self.assertFalse(any(any(bad in ref for bad in ('SolidWorks', 'Windows.Forms', 'Presentation', 'WindowsBase')) for ref in refs), layer)
    def test_core_and_application_have_no_concrete_adapters(self):
        for layer in ('Core', 'Application'):
            directory, _ = self.project(layer)
            for source in directory.rglob('*.cs'):
                text = source.read_text(encoding='utf-8-sig')
                self.assertNotRegex(text, r'SolidWorks\.Interop|System\.Windows|System\.Diagnostics|\b(?:File|Directory)\.(?:Read|Write|Exists|Create|Delete|Move|Open)', str(source))
    def test_reusable_layers_have_no_ui(self):
        for layer in ('Core', 'Application', 'Infrastructure'):
            directory, _ = self.project(layer)
            for source in directory.rglob('*.cs'):
                self.assertNotRegex(source.read_text(encoding='utf-8-sig'), r'SolidWorks\.Interop|System\.Windows|MessageBox|PropertyManagerPage', str(source))
    def test_test_runner_references_real_layers(self):
        text = (ROOT / 'tests/parity/CandidateRunner.csproj').read_text(encoding='utf-8')
        self.assertNotRegex(text, r'<Compile Include="[^\"]*src[\\/]')
        for layer in ('Core', 'Application', 'Infrastructure'):
            self.assertIn('SWSimTool.' + layer + '.csproj', text)
    def test_production_contract_is_native_only(self):
        source = (ROOT / 'src/SWSimTool.Application/Simulation/ProjectSourceCoordinator.cs').read_text(encoding='utf-8')
        contract = source[source.index('public interface ICadSource'):source.index('public sealed class ProjectSourceCoordinator')]
        self.assertIn('BuildNative', contract)
        self.assertNotIn('BuildReference', contract)
        self.assertNotIn('bool', contract)
        pipeline = (ROOT / 'src/SWSimTool.Application/Simulation/NativeExportPipeline.cs').read_text(encoding='utf-8')
        self.assertNotIn('PythonToolBackend', pipeline)
        self.assertNotIn('NativeAssetPlanner', pipeline)
        self.assertIn('validationService.ValidateAsync', pipeline)
        self.assertLess(pipeline.index('validationService.ValidateAsync'), pipeline.index('packages.Publish'))
if __name__ == '__main__': unittest.main(verbosity=2)
