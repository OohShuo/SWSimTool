"""Strict compiled and dynamic comparison across OS/culture artifacts."""
from pathlib import Path
import json
import sys
import math
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tests/parity'))
from semantic_compare import compiled_semantics,dynamics
import mujoco
base=Path(sys.argv[1]) if len(sys.argv)>1 else ROOT/'build/reports/platform'
sets=sorted(base/system/culture for system in ('windows','linux') for culture in ('en-US','zh-CN','de-DE') if (base/system/culture/'test_core_and_extensions_1').exists())
assert len(sets)==6,'Need Windows and Linux outputs for all three cultures'
assert json.loads((base/'windows/storage.json').read_text(encoding='utf-8'))==json.loads((base/'linux/storage.json').read_text(encoding='utf-8')),'Cross-platform frozen storage/typed fields differ'
reference=sets[0]
files=sorted(path.relative_to(reference) for path in reference.rglob('*.xml'))
assert files,'No compiled fixtures'
checks=0
text_differences=[]
def compare_xml_structure(left,right):
    assert left.tag==right.tag and left.attrib.keys()==right.attrib.keys(),'XML structure differs'
    assert (left.text or '').strip()==(right.text or '').strip(),'XML text differs'
    for key,value in left.attrib.items():
        other=right.attrib[key]
        if value==other:continue
        try:
            a=[float(x) for x in value.split()];b=[float(x) for x in other.split()]
        except ValueError:
            raise AssertionError('XML identity/reference differs: '+key)
        assert len(a)==len(b) and all(math.isfinite(x) and math.isfinite(y) and abs(x-y)<=1e-9 for x,y in zip(a,b)),'XML numeric attribute differs: '+key
    assert len(left)==len(right),'XML child count differs'
    for a,b in zip(left,right):compare_xml_structure(a,b)
for folder in sets[1:]:
    assert sorted(p.relative_to(folder) for p in folder.rglob('*.xml'))==files,'Fixture sets differ'
    for relative in files:
        a=mujoco.MjModel.from_xml_path(str(reference/relative))
        b=mujoco.MjModel.from_xml_path(str(folder/relative))
        compiled_semantics(a,b)
        dynamics(a,b,steps=(1,10,100))
        compare_xml_structure(ET.parse(reference/relative).getroot(),ET.parse(folder/relative).getroot())
        if (reference/relative).read_text(encoding='utf-8-sig')!=(folder/relative).read_text(encoding='utf-8-sig'):
            text_differences.append(str(folder.relative_to(base)/relative))
        checks+=1
# Culture must never affect serialized numbers. OS math libraries can differ in
# their final floating-point bits; compiled semantics remain the primary gate.
for system in ('windows','linux'):
    for relative in files:
        texts=[(base/system/culture/relative).read_text(encoding='utf-8-sig') for culture in ('en-US','zh-CN','de-DE')]
        assert len(set(texts))==1,'Culture-dependent XML: '+str(relative)
print(json.dumps(dict(status='passed',sets=len(sets),fixtures=len(files),comparisons=checks,static_tolerance=1e-9,dynamic_tolerance=2e-7,culture_xml_exact=True,cross_os_text_differences=text_differences),indent=2))
