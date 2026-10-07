"""Fault-inject publication receipts in an isolated synthetic directory."""
from pathlib import Path
import subprocess
import tempfile
import unittest
ROOT = Path(__file__).resolve().parents[2]

class ValidationReceipt(unittest.TestCase):
    def test_missing_stale_and_changed_inputs_fail_closed(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'build/test-work') as folder:
            root=Path(folder)
            binary=root/'build/bin/SWSimTool.SolidWorks/Release/net48/fixture.dll'
            script=root/'runtime/python/tool.py'
            cli=root/'build/bin/SWSimTool.Cli/Release/net8.0/fixture.dll'
            for p in (binary,script,cli): p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'first')
            (root/'build/reports').mkdir()
            def run(mode):
                return subprocess.run(['powershell','-NoProfile','-File',str(ROOT/'tools/build/ValidationReceipt.ps1'),'-Mode',mode,'-Root',str(root)],capture_output=True)
            self.assertNotEqual(run('Verify').returncode,0)
            result=run('Write');self.assertEqual(result.returncode,0,result.stderr.decode(errors='replace'))
            self.assertEqual(run('Verify').returncode,0)
            binary.write_bytes(b'changed')
            self.assertNotEqual(run('Verify').returncode,0)
            binary.write_bytes(b'first');script.write_bytes(b'changed')
            self.assertNotEqual(run('Verify').returncode,0)
            script.write_bytes(b'first');(script.parent/'extra.py').write_bytes(b'extra')
            self.assertNotEqual(run('Verify').returncode,0)

if __name__=='__main__':unittest.main(verbosity=2)
