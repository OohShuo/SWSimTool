using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SW2URDF.URDF;

namespace SW2URDF.Simulation
{
    // Persist identity in the saved tree; refresh export names only at the storage boundary.
    public static class StableReferences
    {
        public static void RemapModes(SimulationProject project, LinkNode previous, LinkNode current)
        {
            if(project?.collision==null||previous==null||current==null)return;
            var names=new Dictionary<string,string>();
            Action<LinkNode> visit=null;visit=n=>{names.Add(n.Link.StableId,n.Name);foreach(LinkNode c in n.Nodes)visit(c);};visit(current);
            var modes=project.collision.link_modes;var updated=new Dictionary<string,string>();
            visit=n=>{string mode,name;if(modes.TryGetValue(n.Name,out mode)&&names.TryGetValue(n.Link.StableId,out name))updated[name]=mode;foreach(LinkNode c in n.Nodes)visit(c);};visit(previous);
            foreach(var pair in modes)if(!updated.ContainsKey(pair.Key)&&names.Values.Contains(pair.Key))updated[pair.Key]=pair.Value;
            project.collision.link_modes=updated;
        }
        public static void Normalize(SimulationProject project, LinkNode tree)
        {
            if (project == null || tree == null) return;
            var links = new Dictionary<string, string>();
            var joints = new Dictionary<string, string>();
            Action<LinkNode> visit = null;
            visit = node => {
                links.Add(node.Link.StableId, node.Name);
                if (node.Parent != null && node.Link.Joint != null)
                    joints.Add(node.Link.Joint.StableId, node.Link.Joint.Name);
                foreach (LinkNode child in node.Nodes) visit(child);
            };
            visit(tree);
            foreach (var item in project.attachments) { var r = Resolve(links,item.link_id,item.link); item.link_id=r.Key; item.link=r.Value; }
            foreach (var item in project.actuators) { item.id=Identity(item.id);var r=Resolve(joints,item.joint_id,item.joint);item.joint_id=r.Key;item.joint=r.Value; }
            foreach (var item in project.sensors) item.id=Identity(item.id);
            foreach (var item in project.site_forces) item.id=Identity(item.id);
            foreach (var item in project.joints) { var r=Resolve(joints,item.joint_id,item.joint);item.joint_id=r.Key;item.joint=r.Value; }
            foreach (var item in project.joint_force_limits) { var r=Resolve(joints,item.joint_id,item.joint);item.joint_id=r.Key;item.joint=r.Value; }
            foreach (var item in project.equalities) {
                item.id=Identity(item.id);
                var a=Resolve(joints,item.joint1_id,item.joint1);var b=Resolve(joints,item.joint2_id,item.joint2);
                item.joint1_id=a.Key;item.joint1=a.Value;item.joint2_id=b.Key;item.joint2=b.Value;
                a=Resolve(links,item.body1_id,item.body1);b=Resolve(links,item.body2_id,item.body2);
                item.body1_id=a.Key;item.body1=a.Value;item.body2_id=b.Key;item.body2=b.Value;
            }
            if(project.collision!=null) {
                foreach(var item in project.collision.geometries) {var r=Resolve(links,item.link_id,item.link);item.link_id=r.Key;item.link=r.Value;}
                foreach(var item in project.collision.allowed_pairs) {var a=Resolve(links,item.link1_id,item.link1);var b=Resolve(links,item.link2_id,item.link2);item.link1_id=a.Key;item.link1=a.Value;item.link2_id=b.Key;item.link2=b.Value;}
            }
            project.NormalizeSiteReferences();
        }
        static string Identity(string id) { return string.IsNullOrWhiteSpace(id)?Guid.NewGuid().ToString("N"):id; }
        static KeyValuePair<string,string> Resolve(Dictionary<string,string> objects,string id,string name)
        {
            if(!string.IsNullOrWhiteSpace(id)) {
                string current;
                // A deleted ID must never silently rebind to a new object bearing the old name.
                return new KeyValuePair<string,string>(id,objects.TryGetValue(id,out current)?current:name);
            }
            if(string.IsNullOrWhiteSpace(name))return new KeyValuePair<string,string>(id,name);
            var found=objects.Where(p=>p.Value==name).ToArray();
            if(found.Length>1)throw new InvalidDataException("Ambiguous reference: "+name);
            return found.Length==1?found[0]:new KeyValuePair<string,string>(id,name);
        }
    }
}
