"""Pure source-model adapter. No CAD handles and no simulation configuration."""

from pathlib import Path as _TestPath
import sys as _TestSys
_TestRoot = next(p for p in _TestPath(__file__).resolve().parents if (p / 'SW2URDF.sln').is_file())
_TestSys.path[:0] = [str(_TestRoot / 'runtime/python'), str(_TestRoot / 'tests/reference/python'), str(_TestRoot / 'tests/backend')]
from dataclasses import dataclass
import xml.etree.ElementTree as ET

@dataclass(frozen=True)
class RobotModel:
    links: tuple
    joints: tuple
    roots: frozenset
    base_xml: str

    @classmethod
    def from_urdf(cls, robot, base_xml):
        links=tuple(ET.tostring(link,encoding='unicode') for link in robot.findall('link'))
        joints=tuple(ET.tostring(joint,encoding='unicode') for joint in robot.findall('joint'))
        children={joint.find('child').get('link') for joint in robot.findall('joint')}
        roots=frozenset(link.get('name') for link in robot.findall('link'))-children
        return cls(links,joints,roots,base_xml)

    def template(self):
        # Every export starts fresh, so removals cannot leave prior overlay nodes behind.
        return ET.fromstring(self.base_xml)
