"""Isolated build/clean contract tests; no CAD, installation or real compiler."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
POWERSHELL = shutil.which('powershell.exe')


@unittest.skipUnless(POWERSHELL, 'Windows PowerShell required')
class BuildOptions(unittest.TestCase):
    def run_ps(self, script, *args):
        return subprocess.run([POWERSHELL, '-NoProfile', '-ExecutionPolicy', 'Bypass',
                               '-File', str(script), *map(str, args)],
                              capture_output=True, timeout=60)

    def test_clean_preserves_tools_worktrees_and_unknown_cache(self):
        with tempfile.TemporaryDirectory() as temp:
            build = Path(temp) / 'build'
            for directory in ('bin', 'obj', 'reports', 'tools', 'worktrees',
                              'native-incremental-old/cache'):
                folder = build / directory
                folder.mkdir(parents=True)
                (folder / 'keep.txt').write_text('data')
            result = self.run_ps(ROOT / 'tools/build/CleanBuild.ps1', '-BuildDirectory', build)
            self.assertEqual(result.returncode, 0, result.stderr)
            for directory in ('bin', 'obj', 'reports'):
                self.assertFalse((build / directory).exists())
            for directory in ('tools', 'worktrees', 'native-incremental-old/cache'):
                self.assertEqual((build / directory / 'keep.txt').read_text(), 'data')

    def test_clean_protects_embedded_checkout_and_finishes_other_outputs(self):
        with tempfile.TemporaryDirectory() as temp:
            build = Path(temp) / 'build'
            checkout = build / 'test-work' / 'checkout'
            checkout.mkdir(parents=True)
            (checkout / '.git').write_text('gitdir: fixture')
            (checkout / 'source.txt').write_text('preserve')
            (build / 'bin').mkdir()
            (build / 'bin/output.dll').write_text('remove')
            result = self.run_ps(ROOT / 'tools/build/CleanBuild.ps1', '-BuildDirectory', build)
            self.assertNotEqual(result.returncode, 0)
            self.assertTrue((checkout / 'source.txt').exists())
            self.assertFalse((build / 'bin').exists())

    def test_clean_reports_denial_and_continues_without_recursive_delete(self):
        with tempfile.TemporaryDirectory() as temp:
            build = Path(temp) / 'build'
            for directory in ('bin', 'obj'):
                (build / directory).mkdir(parents=True)
                (build / directory / 'output.txt').write_text('output')
            wrapper = Path(temp) / 'deny.ps1'
            wrapper.write_text('''param($Target,$Cleaner)
function Remove-Item {
    param($LiteralPath,[switch]$Force)
    if($LiteralPath.EndsWith('bin\\output.txt')) {throw 'Injected access denied'}
    Microsoft.PowerShell.Management\\Remove-Item -LiteralPath $LiteralPath -Force:$Force
}
& $Cleaner -BuildDirectory $Target
''')
            result = self.run_ps(wrapper, '-Target', build, '-Cleaner', ROOT / 'tools/build/CleanBuild.ps1')
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b'Injected access denied', result.stdout)
            self.assertTrue((build / 'bin/output.txt').exists())
            self.assertFalse((build / 'obj').exists())

    def test_clean_inaccessible_test_cache_warns_without_failing(self):
        with tempfile.TemporaryDirectory() as temp:
            build = Path(temp) / 'build'
            cache = build / 'test-work/final-backend-cache/projects/denied'
            cache.mkdir(parents=True)
            (cache / 'keep.txt').write_text('cache')
            (build / 'bin').mkdir()
            (build / 'bin/output.dll').write_text('remove')
            wrapper = Path(temp) / 'deny-cache.ps1'
            wrapper.write_text('''param($Target,$Cleaner)
function Get-ChildItem {
    param($LiteralPath,[switch]$Force)
    if($LiteralPath.EndsWith('projects\\denied')) {throw (New-Object UnauthorizedAccessException('Injected cache access denied'))}
    Microsoft.PowerShell.Management\\Get-ChildItem -LiteralPath $LiteralPath -Force:$Force
}
& $Cleaner -BuildDirectory $Target
''')
            result = self.run_ps(wrapper, '-Target', build, '-Cleaner', ROOT / 'tools/build/CleanBuild.ps1')
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn(b'Temporary test output retained', result.stdout)
            self.assertIn(b'temporary test files remain', result.stdout)
            self.assertEqual((cache / 'keep.txt').read_text(), 'cache')
            self.assertFalse((build / 'bin').exists())

    def test_clean_does_not_follow_junction(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            outside = root / 'outside'
            outside.mkdir()
            (outside / 'keep.txt').write_text('preserve')
            link = root / 'build/bin/link'
            link.parent.mkdir(parents=True)
            creator = root / 'junction.ps1'
            creator.write_text('param($Link,$Target)\nNew-Item -ItemType Junction -Path $Link -Target $Target -ErrorAction Stop | Out-Null')
            created = self.run_ps(creator, '-Link', link, '-Target', outside)
            self.assertEqual(created.returncode, 0, created.stderr)
            try:
                result = self.run_ps(ROOT / 'tools/build/CleanBuild.ps1', '-BuildDirectory', root / 'build')
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual((outside / 'keep.txt').read_text(), 'preserve')
                self.assertTrue(link.exists())
            finally:
                os.rmdir(link)  # Remove the fixture junction itself, never its target.

    def make_fixture(self, root):
        for name in ('build.ps1', 'tools/build/AssembleRelease.ps1', 'tools/build/ValidationReceipt.ps1', 'tools/build/GetSha256.ps1'):
            dest = root / name
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / name, dest)
        for directory in ('tests/architecture', 'tools/docs', 'sdk', 'INSTALL', 'docs/guide-images'):
            (root / directory).mkdir(parents=True, exist_ok=True)
        (root / 'sdk/SolidWorks.Interop.sldworks.dll').write_text('fixture')
        (root / 'Version.props').write_text('<Project><PropertyGroup><SWSimToolVersion>0.0.0</SWSimToolVersion></PropertyGroup></Project>')
        (root / 'LICENSE').write_text('fixture license')
        (root / 'docs/INSTALL.md').write_text('fixture guide')
        (root / 'docs/SWSimTool_0.0.0_guide.md').write_text('fixture guide')
        (root / 'docs/guide-images/fixture.png').write_bytes(b'image')
        (root / 'runtime/python').mkdir(parents=True)
        (root / 'runtime/python/tool.py').write_text('# fixture')
        (root / 'runtime/python/requirements.txt').write_text('')
        (root / 'tools/docs/Build-Guide.py').write_text("from pathlib import Path\np=Path('build/docs');p.mkdir(parents=True,exist_ok=True);(p/'SWSimTool_0.0.0_guide.html').write_text('guide')")
        for name in ('Test-ReleaseLayout.py', 'Test-CleanWorkspace.py'):
            (root / 'tests/architecture' / name).write_text("print('fixture check')")
        (root / 'tools/build/RunTests.ps1').write_text("throw 'Regression sentinel: must not run when skipped'")
        compiler = root / 'compiler.ps1'
        compiler.write_text('''$root=$PSScriptRoot
$args | Out-File "$root/compile-args.txt" -Append
$output="$root/build/bin/SWSimTool.SolidWorks/Release/net48"
New-Item -ItemType Directory -Path "$output/images" -Force | Out-Null
foreach($name in @('SWSimTool.dll','SWSimTool.Core.dll','SWSimTool.Application.dll','SWSimTool.Infrastructure.dll','SWSimTool.pdb','CsvHelper.dll','MathNet.Numerics.dll','log4net.dll','solidworkstools.dll','SWSimTool.png')) {
    [IO.File]::WriteAllText("$output/$name",'fixture')
}
[IO.File]::WriteAllText("$output/images/fixture.png",'fixture')
exit 0
''')
        installer = root / 'iscc.ps1'
        installer.write_text('''[IO.File]::WriteAllBytes("$PSScriptRoot/build/dist/SWSimTool_fixture_Setup.exe",(New-Object byte[] 100001))
exit 0
''')
        return ['-SolidWorksDir', root / 'sdk', '-Python', os.sys.executable,
                '-MSBuild', compiler, '-ISCC', installer]

    def test_skip_tests_installer_without_receipt(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            args = self.make_fixture(root)
            result = self.run_ps(root / 'build.ps1', '-Installer', '-SkipTests', *args)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertTrue((root / 'build/dist/SWSimTool_fixture_Setup.exe.sha256').exists())
            self.assertFalse((root / 'build/reports/validation-Release.json').exists())
            receipt = json.loads((root / 'build/reports/package-Release.json').read_text(encoding='utf-8-sig'))
            self.assertEqual(receipt['tests'], 'NOT RUN')
            calls = (root / 'compile-args.txt').read_text(encoding='utf-16')
            self.assertNotIn('CoreTests.csproj', calls)
            self.assertNotIn('CandidateRunner.csproj', calls)

    def test_default_installer_still_runs_regressions(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            args = self.make_fixture(root)
            result = self.run_ps(root / 'build.ps1', '-Installer', *args)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b'Regression sentinel', result.stderr)
            self.assertFalse((root / 'build/runtime-release').exists())

    def test_skip_package_and_default_assembly_receipt_gate(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            args = self.make_fixture(root)
            result = self.run_ps(root / 'build.ps1', '-Package', '-SkipTests', *args)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertTrue((root / 'build/runtime-release/SWSimTool.dll').exists())
            self.assertFalse((root / 'build/dist/SWSimTool_fixture_Setup.exe').exists())
            result = self.run_ps(root / 'tools/build/AssembleRelease.ps1', '-Python', os.sys.executable)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b'Mandatory tests have not passed', result.stderr)

    def test_invalid_skip_combinations_fail_before_build(self):
        for args in (('-SkipTests',), ('-SkipTests', '-Test', '-Installer'),
                     ('-SkipTests', '-Clean', '-Installer')):
            result = self.run_ps(ROOT / 'build.ps1', *args)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b'-SkipTests requires', result.stderr)


if __name__ == '__main__':
    unittest.main(verbosity=2)
