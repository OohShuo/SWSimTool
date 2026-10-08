using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SWSimTool.URDF;

namespace SWSimTool.Simulation
{
    // Persist identity in the saved tree; refresh export names only at the storage boundary.
    public static class StableReferences
    {
        public static Dictionary<string,string> LinkNames(LinkNode tree)
        {
            ValidateIdentities(tree,false);
            var names=new Dictionary<string,string>();
            if(tree!=null){Action<LinkNode> visit=null;visit=n=>{names.Add(n.Link.StableId,n.Name);foreach(LinkNode c in n.Nodes)visit(c);};visit(tree);}return names;
        }
        public static void MigrateLegacyLinkModesByName(SimulationProject project,IDictionary<string,string> namesById)=>DomainStableReferences.MigrateLegacyLinkModesByName(project,namesById);
        public static void RemapLinkModesByStableId(SimulationProject project)=>DomainStableReferences.RemapLinkModesByStableId(project);
        public static void Normalize(SimulationProject project, LinkNode tree)
        {
            NormalizeTree(tree);
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
            MigrateLegacyLinkModesByName(project,links);
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
            // An unresolved legacy name gets a persistent tombstone, never a future name retry.
            return found.Length==1?found[0]:new KeyValuePair<string,string>(Guid.NewGuid().ToString("N"),name);
        }
        public static void NormalizeTree(LinkNode tree)
        {
            if(tree==null)return;
            ValidateIdentities(tree,false);
            var nodes=new List<LinkNode>();Action<LinkNode> visit=null;visit=n=>{nodes.Add(n);foreach(LinkNode c in n.Nodes)visit(c);};visit(tree);
            var joints=nodes.Where(n=>n.Parent!=null).ToDictionary(n=>n.Link.Joint.StableId,n=>n.Link.Joint.Name);
            foreach(var node in nodes){var mimic=node.Link.Joint?.Mimic;if(mimic==null||!mimic.ElementContainsData())continue;var r=Resolve(joints,mimic.SourceJointId,mimic.JointName);mimic.SourceJointId=r.Key;mimic.JointName=r.Value;}
        }
        public static void ValidateIdentities(LinkNode tree,bool checkNames=true)
        {
            if(tree==null)return;
            var links=new Dictionary<string,string>();var names=new Dictionary<string,string>();
            var joints=new Dictionary<string,string>();var jointNames=new Dictionary<string,string>();
            Action<Dictionary<string,string>,string,string,string> add=(map,key,path,kind)=>{
                if(string.IsNullOrWhiteSpace(key))return;string previous;
                if(map.TryGetValue(key,out previous))throw new InvalidDataException("重复 "+kind+" '"+key+"'："+previous+" 与 "+path+"。请修正冲突；不会丢弃或覆盖对象。");
                map.Add(key,path);
            };
            Action<LinkNode,string> visit=null;visit=(node,parent)=>{
                string path=parent+"/"+node.Name;
                if(node.Link==null)throw new InvalidDataException("缺少 link 对象："+path);
                add(links,node.Link.StableId,path,"link ID");if(checkNames)add(names,node.Name,path,"link 名称");
                if(node.Parent!=null&&node.Link.Joint!=null){add(joints,node.Link.Joint.StableId,path,"joint ID");if(checkNames)add(jointNames,node.Link.Joint.Name,path,"joint 名称");}
                foreach(LinkNode child in node.Nodes)visit(child,path);
            };visit(tree,"");
        }
        public static void ValidateTree(LinkNode tree)
        {
            NormalizeTree(tree);if(tree==null)return;var joints=new Dictionary<string,string>();Action<LinkNode> collect=null;collect=n=>{if(n.Parent!=null)joints.Add(n.Link.Joint.StableId,n.Link.Joint.Name);foreach(LinkNode c in n.Nodes)collect(c);};collect(tree);
            Action<LinkNode> check=null;check=n=>{var mimic=n.Link.Joint?.Mimic;if(mimic!=null&&mimic.ElementContainsData())SimulationConfigBuilder.Reference(mimic.SourceJointId,mimic.JointName,joints);foreach(LinkNode c in n.Nodes)check(c);};check(tree);
        }
    }
}
