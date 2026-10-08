"""Portable CLI contract and old storage cross-target equality; no CAD access."""
from pathlib import Path
import json
import os
import subprocess
import sys
import tempfile
import unittest
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tests/backend'))
from test_convert import URDF
CLI=['dotnet',str(ROOT/'build/bin/SWSimTool.Cli/Release/net8.0/SWSimTool.Cli.dll')]
NET8=['dotnet',str(ROOT/'build/bin/CandidateRunner/Release/net8.0/SWSimTool.CandidateRunner.dll')]
NET48=[str(ROOT/'build/bin/CandidateRunner/Release/net48/SWSimTool.CandidateRunner.exe')]

class ModernHost(unittest.TestCase):
    def run_command(self, command, *args):
        return subprocess.run(command+list(map(str,args)),capture_output=True,text=True,encoding='utf-8')

    def test_storage_golden_and_all_fields_cross_target(self):
        with tempfile.TemporaryDirectory(dir=ROOT/'build/test-work') as name:
            outputs=[]
            source=ROOT/'tests/compatibility/sw2mujoco_v2/legacy_v2_document.json'
            for i, command in enumerate((NET48,NET8) if os.name=='nt' else (NET8,)):
                output=Path(name)/f'{i}.json'
                result=self.run_command(command,'--storage',source,output)
                self.assertEqual(result.returncode,0,result.stderr)
                data=json.loads(output.read_text(encoding='utf-8-sig'))
                self.assertEqual(data['raw'],json.loads(source.read_text(encoding='utf-8-sig')))
                outputs.append(data)
            if len(outputs)==2:self.assertEqual(*outputs)
            artifact=os.getenv('SWSIMTOOL_STORAGE_ARTIFACT')
            if artifact:Path(artifact).write_text(json.dumps(outputs[-1],ensure_ascii=False),encoding='utf-8')

    def test_cli_failure_contract_and_unicode_export(self):
        self.assertEqual(self.run_command(CLI,'--help').returncode,0)
        for args in ([],['unknown'],['export','--urdf'],['inspect','--oops','x'],['inspect','--urdf','a','--urdf','b']):
            self.assertEqual(self.run_command(CLI,*args).returncode,2,args)
        with tempfile.TemporaryDirectory(prefix='SWSimTool 中文 空格-',dir=ROOT/'build/test-work') as name:
            folder=Path(name);urdf=folder/'机器人.urdf';urdf.write_text(URDF,encoding='utf-8')
            result=self.run_command(CLI,'inspect','--urdf',urdf)
            self.assertEqual(result.returncode,0,result.stderr)
            self.assertEqual(json.loads(result.stdout)['links'],3)
            requested=folder/'out'/'机器人.xml'
            result=self.run_command(CLI,'export','--urdf',urdf,'--output',requested,'--python',sys.executable)
            self.assertEqual(result.returncode,0,result.stderr)
            outputs=list((folder/'out').rglob('*.xml'))
            self.assertEqual(len(outputs),1)
            output=outputs[0];before=output.read_bytes()
            result=self.run_command(CLI,'validate','--mjcf',output,'--python',sys.executable)
            self.assertEqual(result.returncode,0,result.stderr)
            result=self.run_command(CLI,'export','--urdf',urdf,'--output',requested,'--python',folder/'missing-python.exe')
            self.assertNotEqual(result.returncode,0)
            self.assertEqual(output.read_bytes(),before)
            self.assertTrue(all(p.suffix in ('.xml','.stl') for p in output.parent.rglob('*') if p.is_file()))
            invalid=folder/'invalid.xml';invalid.write_text('<broken>',encoding='utf-8')
            self.assertNotEqual(self.run_command(CLI,'validate','--mjcf',invalid,'--python',sys.executable).returncode,0)
            self.assertNotEqual(self.run_command(CLI,'inspect','--urdf',folder/'missing.urdf').returncode,0)

    def test_net8_domain_and_tool_faults(self):
        for args in (['--selftest'],['--tooltest',sys.executable,ROOT/'tests/tools/fake_tool.py']):
            result=self.run_command(NET8,*args)
            self.assertEqual(result.returncode,0,result.stderr+result.stdout)

if __name__=='__main__':unittest.main(verbosity=2)
