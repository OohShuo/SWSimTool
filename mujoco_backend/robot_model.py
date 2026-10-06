"""Pure source-model adapter. No CAD handles and no simulation configuration."""
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
