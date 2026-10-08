"""Filesystem identity and atomic-publish guards using owned temporary inputs."""
from pathlib import Path
import os
import subprocess
import sys
import tempfile
import unittest
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tests/backend'))
from test_convert import URDF
FIXTURE=['dotnet',str(ROOT/'build/bin/CandidateRunner/Release/net8.0/SWSimTool.CandidateRunner.dll')]

class Paths(unittest.TestCase):
    def export(self,urdf,output):
        return subprocess.run(FIXTURE+['--local-export',sys.executable,str(urdf),str(output)],capture_output=True,text=True,encoding='utf-8')

    @unittest.skipIf(os.name=='nt','Case-sensitive filesystem identity is a Linux gate')
    def test_same_casefold_unrelated_file_is_never_deleted(self):
        with tempfile.TemporaryDirectory(dir=ROOT/'build/test-work') as name:
            base=Path(name);urdf=base/'机器人.urdf';urdf.write_text(URDF,encoding='utf-8')
            output=base/'robot_mjcf/robot.xml'
            result=self.export(urdf,output);self.assertEqual(result.returncode,0,result.stderr)
            before=output.read_bytes();extra=output.with_name('ROBOT.XML');extra.write_bytes(b'user content')
            result=self.export(urdf,output);self.assertNotEqual(result.returncode,0)
            self.assertEqual(extra.read_bytes(),b'user content');self.assertEqual(output.read_bytes(),before)

    @unittest.skipIf(os.name=='nt','Unix symbolic-link filesystem gate')
    def test_linked_package_refused_without_mutating_target(self):
        with tempfile.TemporaryDirectory(dir=ROOT/'build/test-work') as name:
            base=Path(name);urdf=base/'robot.urdf';urdf.write_text(URDF,encoding='utf-8')
            original=base/'original_mjcf/robot.xml'
            result=self.export(urdf,original);self.assertEqual(result.returncode,0,result.stderr)
            before=original.read_bytes();linked=base/'linked_mjcf';linked.symlink_to(original.parent,target_is_directory=True)
            result=self.export(urdf,linked/'robot.xml');self.assertNotEqual(result.returncode,0)
            self.assertEqual(original.read_bytes(),before);self.assertTrue(linked.is_symlink())

if __name__=='__main__':unittest.main(verbosity=2)
