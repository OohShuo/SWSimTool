"""Generated artifacts belong to build; immutable historical fixtures are inputs."""
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[2]
class CleanWorkspace(unittest.TestCase):
    def test_source_tree_has_no_generated_artifacts(self):
        unwanted = []
        for folder in ('src', 'tests', 'runtime', 'tools'):
            for path in (ROOT / folder).rglob('*'):
                if path.name in ('bin', 'obj', '__pycache__', '.pytest_cache'):
                    unwanted.append(str(path.relative_to(ROOT)))
                if path.suffix in ('.log', '.pyc', '.binlog', '.tmp'):
                    unwanted.append(str(path.relative_to(ROOT)))
        self.assertEqual(unwanted, [])
    def test_installer_reads_assembled_payload(self):
        script = (ROOT / 'INSTALL/install.iss').read_text(encoding='utf-8')
        self.assertIn('runtime-release', script)
        for line in script.splitlines():
            if line.startswith(('Source:', 'LicenseFile=', 'InfoBeforeFile=')):
                self.assertIn('{#Payload}', line)
        self.assertIn('OutputDir=..\\build\\dist', script)
if __name__ == '__main__': unittest.main(verbosity=2)
