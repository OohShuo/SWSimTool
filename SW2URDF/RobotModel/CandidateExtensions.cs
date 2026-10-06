using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SW2URDF.RobotModel
{
    // Candidate-only adapter for the current persisted configuration schema.
    // The bridge is private and rebuilt on each export; it cannot mutate a snapshot.
    internal static class CandidateExtensions
    {
        static void Set(XElement e,string key,double value) { e.SetAttributeValue(key,Numbers.Format(value)); }
        static XElement Group(XElement root,string name) { var g=root.Element(name);if(g==null){g=new XElement(name);root.Add(g);}return g; }
        static double Nonnegative(Dictionary<string,object> c,string key,double fallback) { var v=Config.N(c,key,fallback);if(v<0)throw new InvalidDataException("Negative "+key);return v; }
        static void Range(XElement e,string key,Dictionary<string,object> c,string low,string high) { var a=Config.N(c,low,double.NaN);var b=Config.N(c,high,double.NaN);if(a>=b)throw new InvalidDataException("Invalid "+key);e.SetAttributeValue(key,Numbers.Text(new[]{a,b})); }
        static void Constraint(XElement e,Dictionary<string,object> c,bool contact,string suffix="")
        {
            if(c==null)return;var t=Config.N(c,"timeconst",contact?.003:.005);var d=Config.N(c,"dampratio",1);var min=Config.N(c,"dmin",.99);var max=Config.N(c,"dmax",contact?.995:.99);var width=Config.N(c,"width",.001);var midpoint=Config.N(c,"midpoint",.5);var power=Config.N(c,"power",2);
            if(t<=0||d<=0||min<=0||max>=1||min>max||width<=0||midpoint<=0||midpoint>=1||power<1)throw new InvalidDataException("Invalid constraint parameters");
            e.SetAttributeValue("solref"+suffix,Numbers.Text(new[]{t,d}));e.SetAttributeValue("solimp"+suffix,Numbers.Text(new[]{min,max,width,midpoint,power}));
            if(contact){Set(e,"margin",Nonnegative(c,"margin",.001));var condim=Config.N(c,"condim",3);if(!new[]{1.0,3,4,6}.Contains(condim))throw new InvalidDataException("Invalid condim");Set(e,"condim",condim);}
        }
        static Dictionary<string,object> Effective(Dictionary<string,object> config,string kind,Dictionary<string,object> local=null)
        { if(local!=null)return local;var solver=Config.D(config,"solver");return Config.B(solver,"enabled",false)?Config.D(solver,kind)??new Dictionary<string,object>():null; }
        static void Unique(IEnumerable<Dictionary<string,object>> items,string key="name") { RobotModelValidator.Unique(items.Select(i=>Config.S(i,key,null)),key); }
        public static void Apply(XElement root,RobotModel model,Dictionary<string,object> config,Dictionary<string,XElement> bodies,Dictionary<string,XElement> joints)
        {
            var settings=Config.Items(config,"joints").ToArray();var actuators=Config.Items(config,"actuators").ToArray();Unique(settings,"joint");Unique(actuators);
            var driven=new HashSet<string>(actuators.Select(a=>Config.S(a,"joint",null)));
            if(Config.B(config,"joint_defaults",false))foreach(var pair in joints){var own=settings.SingleOrDefault(s=>Config.S(s,"joint",null)==pair.Key);var defaults=driven.Contains(pair.Key)?new[]{.01,.01,.001}:new[]{.001,.001,0};var keys=new[]{"damping","frictionloss","armature"};for(int i=0;i<3;i++)if(own==null||!own.ContainsKey(keys[i])||own[keys[i]]==null)Set(pair.Value,keys[i],defaults[i]);}
            foreach(var s in settings){var name=Config.S(s,"joint",null);if(!joints.ContainsKey(name))throw new InvalidDataException("Unknown scalar joint: "+name);var j=joints[name];var type=Config.S(s,"type","inherit");var limit=Config.S(s,"limit_mode","inherit");if(!new[]{"inherit","none","custom"}.Contains(limit))throw new InvalidDataException("Invalid limit mode");
                if(type!="inherit"){if(type!="hinge"&&type!="slide")throw new InvalidDataException("Invalid joint override");if(type!=(string)j.Attribute("type")&&limit=="inherit")throw new InvalidDataException("Changing joint type requires explicit limits");j.SetAttributeValue("type",type);}
                foreach(var key in new[]{"damping","frictionloss","armature","stiffness","springref","ref","margin"})if(s.ContainsKey(key)&&s[key]!=null)Set(j,key,key=="springref"||key=="ref"?Config.N(s,key,0):Nonnegative(s,key,0));
                if(limit=="none"){j.SetAttributeValue("limited","false");j.Attribute("range")?.Remove();}else if(limit=="custom"){j.SetAttributeValue("limited","true");Range(j,"range",s,"lower","upper");}
                Constraint(j,Config.D(s,"limit_solver"),false,"limit");Constraint(j,Config.D(s,"friction_solver"),false,"friction");
                if((s.ContainsKey("pos")&&s["pos"]!=null)||(s.ContainsKey("axis")&&s["axis"]!=null))throw new NotSupportedException("Joint position/axis override is not part of candidate model");
            }
            var forces=Config.Items(config,"joint_force_limits").ToArray();Unique(forces,"joint");foreach(var f in forces){var name=Config.S(f,"joint",null);if(!joints.ContainsKey(name))throw new InvalidDataException("Unknown force-limited joint");var j=joints[name];j.SetAttributeValue("actuatorfrclimited","true");Range(j,"actuatorfrcrange",f,"lower","upper");}
            var mode=Config.S(config,"base_mode","inherit");if(!new[]{"inherit","fixed","floating"}.Contains(mode))throw new InvalidDataException("Invalid base mode");if(mode=="floating"){
                var body=bodies[model.Core.Root.Id];if(body.Name!="body")throw new InvalidDataException("Missing floating root body");if(root.Descendants().Any(e=>(string)e.Attribute("name")=="__sw2mujoco_base_free"))throw new InvalidDataException("Reserved free joint name");body.AddFirst(new XElement("freejoint",new XAttribute("name","__sw2mujoco_base_free")));
            }
            var solver=Config.D(config,"solver");var enabled=Config.B(solver,"enabled",false);
            var overrides=settings.Any(s=>Config.D(s,"limit_solver")!=null||Config.D(s,"friction_solver")!=null)||Config.Items(config,"equalities").Any(e=>Config.D(e,"solver")!=null)||Config.Items(Config.D(config,"collision")??new Dictionary<string,object>(),"allowed_pairs").Any(p=>Config.D(p,"solver")!=null);
            if(enabled||overrides){var option=Group(root,"option");option.Add(new XElement("flag",new XAttribute("refsafe","enable")));if(enabled){option.SetAttributeValue("solver","Newton");option.SetAttributeValue("cone","elliptic");var dt=Config.N(solver,"timestep",.001);var iter=Config.N(solver,"iterations",100);var no=Config.N(solver,"noslip_iterations",0);var ratio=Config.N(solver,"impratio",10);if(dt<=0||iter<1||iter!=Math.Floor(iter)||no<0||no!=Math.Floor(no)||ratio<=0)throw new InvalidDataException("Invalid solver settings");Set(option,"timestep",dt);Set(option,"iterations",iter);Set(option,"noslip_iterations",no);Set(option,"impratio",ratio);Set(option,"tolerance",Nonnegative(solver,"tolerance",1e-9));foreach(var g in root.Descendants("geom"))if((string)g.Attribute("contype")!="0"||(string)g.Attribute("conaffinity")!="0")Constraint(g,Effective(config,"contact"),true);}}
            Collision(root,model,config,bodies);
            foreach(var a in actuators){var type=Config.S(a,"type",null);if(!new[]{"motor","position","velocity"}.Contains(type))throw new NotSupportedException("Actuator type: "+type);var joint=Config.S(a,"joint",null);if(!joints.ContainsKey(joint))throw new InvalidDataException("Unknown actuator joint");var e=new XElement(type,new XAttribute("name",Config.S(a,"name",null)),new XAttribute("joint",joint),new XAttribute("ctrllimited","true"),new XAttribute("forcelimited","true"));Set(e,"gear",Config.N(a,"gear",1));Range(e,"ctrlrange",a,"ctrl_min","ctrl_max");Range(e,"forcerange",a,"force_min","force_max");if(type!="motor")Set(e,type=="position"?"kp":"kv",Nonnegative(a,"gain",1));Group(root,"actuator").Add(e);}
            Sensors(root,model,config,bodies);
            Equalities(root,model,config,bodies,joints);
            SiteForces(root,model,config);
            // Names are unique within each MuJoCo object namespace, including generated names.
            foreach(var kind in new[]{"body","joint","geom","site","camera"})RobotModelValidator.Unique(root.Descendants(kind).Where(e=>e.Attribute("name")!=null).Select(e=>(string)e.Attribute("name")),kind+" name");
            foreach(var kind in new[]{"actuator","sensor","equality","tendon"})RobotModelValidator.Unique(root.Elements(kind).Elements().Where(e=>e.Attribute("name")!=null).Select(e=>(string)e.Attribute("name")),kind+" name");
        }
        static void Sensors(XElement root,RobotModel model,Dictionary<string,object> config,Dictionary<string,XElement> bodies)
        {
            var sensors=Config.Items(config,"sensors").ToArray();Unique(sensors);
            foreach(var s in sensors){var name=Config.S(s,"name",null);var site=FindSite(model,s,"site","site_id");var type=Config.S(s,"type",null);if(!site.IsFrame)throw new InvalidDataException("Sensor requires an oriented frame");if(type=="camera"){var fovy=Config.N(s,"fovy",45);if(fovy<=0||fovy>=180)throw new InvalidDataException("Invalid camera field of view");var camera=new XElement("camera",new XAttribute("name",name),new XAttribute("fovy",Numbers.Format(fovy)));MjcfExporter.Pose(camera,site.LinkFromSite);bodies[site.LinkId].Add(camera);}
                else{if(type!="imu"&&type!="tof")throw new NotSupportedException("Sensor type: "+type);foreach(var kind in type=="imu"?new[]{"accelerometer","gyro"}:new[]{"rangefinder"})Group(root,"sensor").Add(new XElement(kind,new XAttribute("name",name+"_"+kind),new XAttribute("site",site.Name),new XAttribute("cutoff",Numbers.Format(Nonnegative(s,"cutoff",0)))));}}
        }
        static SiteSnapshot FindSite(RobotModel model,Dictionary<string,object> c,string nameKey,string idKey)
        {
            var id=Config.S(c,idKey,"");var name=Config.S(c,nameKey,"");var site=id!=""?model.Simulation.Sites.SingleOrDefault(s=>s.Id==id):model.Simulation.Sites.SingleOrDefault(s=>s.Name==name);if(site==null)throw new InvalidDataException("Unknown site: "+name);return site;
        }
        static void Equalities(XElement root,RobotModel model,Dictionary<string,object> config,Dictionary<string,XElement> bodies,Dictionary<string,XElement> joints)
        {
            var values=Config.Items(config,"equalities").ToArray();Unique(values);
            foreach(var v in values){var type=Config.S(v,"type",null);if(!new[]{"connect","weld","joint"}.Contains(type))throw new NotSupportedException("Equality type");var e=new XElement(type,new XAttribute("name",Config.S(v,"name",null)),new XAttribute("active",Config.B(v,"active",true)?"true":"false"));Constraint(e,Effective(config,"equality",Config.D(v,"solver")),false);
                if(type=="joint"){var a=Config.S(v,"joint1",null);var b=Config.S(v,"joint2","");if(!joints.ContainsKey(a)||(b!=""&&(!joints.ContainsKey(b)||a==b)))throw new InvalidDataException("Invalid equality joint");e.SetAttributeValue("joint1",a);if(b!="")e.SetAttributeValue("joint2",b);e.SetAttributeValue("polycoef",Numbers.Text(Config.V(v,"polycoef",5,new[]{0.0,1,0,0,0})));}
                else{var binding=Config.S(v,"binding","site");if(binding=="site"){var a=FindSite(model,v,"site1","site1_id");var b=FindSite(model,v,"site2","site2_id");if(a.Id==b.Id||a.LinkId==b.LinkId||(type=="weld"&&(!a.IsFrame||!b.IsFrame)))throw new InvalidDataException("Invalid equality sites");e.SetAttributeValue("site1",a.Name);e.SetAttributeValue("site2",b.Name);}
                    else if(binding=="body"){var a=Config.S(v,"body1",null);var b=Config.S(v,"body2","");if(!bodies.ContainsKey(a)||(b!=""&&(!bodies.ContainsKey(b)||a==b)))throw new InvalidDataException("Invalid equality bodies");e.SetAttributeValue("body1",bodies[a].Attribute("name").Value);if(b!="")e.SetAttributeValue("body2",bodies[b].Attribute("name").Value);
                        if(type=="connect")e.SetAttributeValue("anchor",Numbers.Text(Config.V(v,"anchor",3,new double[3])));else{var pose=Config.S(v,"pose_mode","inherit");if(pose=="custom"){var p=Config.V(v,"position",3,null);var r=Config.V(v,"orientation",3,null);e.SetAttributeValue("relpose",Numbers.Text(p)+" "+Quaterniond.FromRpy(new Vector3d(r[0],r[1],r[2])).ToString());}else if(pose!="inherit")throw new InvalidDataException("Invalid weld pose mode");}}
                    else throw new InvalidDataException("Invalid equality binding");if(type=="weld")Set(e,"torquescale",Nonnegative(v,"torquescale",1));}
                Group(root,"equality").Add(e);
            }
        }
        static void SiteForces(XElement root,RobotModel model,Dictionary<string,object> config)
        {
            var values=Config.Items(config,"site_forces").ToArray();Unique(values);var world=new Dictionary<string,RigidTransform>();world[model.Core.Root.Id]=RigidTransform.Identity;Action<string> visit=null;visit=id=>{foreach(var j in model.Core.Joints.Where(j=>j.ParentLinkId==id)){world[j.ChildLinkId]=world[id]*j.ParentLinkFromJoint;visit(j.ChildLinkId);}};visit(model.Core.Root.Id);
            foreach(var v in values){var name=Config.S(v,"name",null);var type=Config.S(v,"type","pull");if(!new[]{"pull","push","spring"}.Contains(type))throw new NotSupportedException("Two-site force type");var a=FindSite(model,v,"site1","site1_id");var b=FindSite(model,v,"site2","site2_id");if(a.Id==b.Id||a.LinkId==b.LinkId)throw new InvalidDataException("Invalid force endpoints");var k=Nonnegative(v,"stiffness",0);var damping=Nonnegative(v,"damping",0);var magnitude=Nonnegative(v,"magnitude",0);if(!Config.B(v,"enabled",true))continue;
                var gap=(world[a.LinkId].Apply(a.LinkFromSite.Translation)-world[b.LinkId].Apply(b.LinkFromSite.Translation)).Length;if((type=="spring"?(k>0||damping>0):magnitude>0)&&gap<=1e-9)throw new InvalidDataException("Two-site force endpoints coincide initially");
                var path=new XElement("spatial",new XAttribute("name","sw2mujoco_tendon_"+name),new XAttribute("limited","false"),new XAttribute("stiffness","0"),new XAttribute("damping","0"),new XAttribute("frictionloss","0"),new XAttribute("width",".001"),new XElement("site",new XAttribute("site",a.Name)),new XElement("site",new XAttribute("site",b.Name)));
                if(type=="spring"){Set(path,"stiffness",k);Set(path,"damping",damping);var lengthMode=Config.S(v,"length_mode","initial");if(lengthMode!="custom"&&lengthMode!="initial")throw new InvalidDataException("Invalid spring length mode");Set(path,"springlength",lengthMode=="custom"?Nonnegative(v,"rest_length",.1):gap);}
                else Group(root,"actuator").Add(new XElement("general",new XAttribute("name","sw2mujoco_force_"+name),new XAttribute("tendon","sw2mujoco_tendon_"+name),new XAttribute("gear","1"),new XAttribute("dyntype","none"),new XAttribute("gaintype","fixed"),new XAttribute("gainprm","0"),new XAttribute("biastype","affine"),new XAttribute("biasprm",Numbers.Format(type=="pull"?-magnitude:magnitude)+" 0 0"),new XAttribute("ctrllimited","false"),new XAttribute("forcelimited","false")));
                Group(root,"tendon").Add(path);
            }
        }
        static void Collision(XElement root,RobotModel model,Dictionary<string,object> config,Dictionary<string,XElement> bodies)
        {
            var rules=Config.D(config,"collision");if(rules==null)return;var modes=Config.D(rules,"link_modes")??new Dictionary<string,object>();foreach(var name in modes.Keys)if(!bodies.ContainsKey(name))throw new InvalidDataException("Unknown collision link");
            var active=new Dictionary<string,List<string>>();foreach(var b in bodies){var mode=Config.S(modes,b.Key,"mesh");if(!new[]{"mesh","primitive","none"}.Contains(mode))throw new InvalidDataException("Invalid collision mode");active[b.Key]=new List<string>();foreach(var g in b.Value.Elements("geom")){if(mode!="mesh"){g.SetAttributeValue("contype","0");g.SetAttributeValue("conaffinity","0");}else if((string)g.Attribute("contype")!="0"||(string)g.Attribute("conaffinity")!="0")active[b.Key].Add(g.Attribute("name").Value);}}
            var geometries=Config.Items(rules,"geometries").ToArray();Unique(geometries);foreach(var v in geometries){var link=Config.S(v,"link",null);if(!bodies.ContainsKey(link))throw new InvalidDataException("Unknown collision geometry link");var type=Config.S(v,"type",null);var length=type=="box"?3:type=="sphere"?1:type=="cylinder"||type=="capsule"?2:0;if(length==0)throw new NotSupportedException("Collision geometry type");var size=Config.V(v,"size",length,null);if(size.Any(x=>x<=0))throw new InvalidDataException("Collision size must be positive");var xyz=Config.V(v,"xyz",3,null);var rpy=Config.V(v,"rpy",3,null);if(Config.S(modes,link,"mesh")!="primitive")continue;if(type=="box")size=size.Select(x=>x/2).ToArray();else if(length==2)size[1]/=2;
                var g=new XElement("geom",new XAttribute("name",Config.S(v,"name",null)),new XAttribute("type",type),new XAttribute("size",Numbers.Text(size)),new XAttribute("group","3"),new XAttribute("mass","0"),new XAttribute("contype","1"),new XAttribute("conaffinity","1"),new XAttribute("rgba","0.2 0.8 1 0.35"));MjcfExporter.Pose(g,new RigidTransform(new Vector3d(xyz[0],xyz[1],xyz[2]),Quaterniond.FromRpy(new Vector3d(rpy[0],rpy[1],rpy[2]))));Constraint(g,Effective(config,"contact"),true);bodies[link].Add(g);active[link].Add(g.Attribute("name").Value);
            }
            foreach(var name in bodies.Keys)if(Config.S(modes,name,"mesh")=="primitive"&&!active[name].Any())throw new InvalidDataException("Primitive mode has no geometry");var contact=Group(root,"contact");var pairs=new Dictionary<string,Dictionary<string,object>>();
            foreach(var p in Config.Items(rules,"allowed_pairs")){var a=Config.S(p,"link1",null);var b=Config.S(p,"link2",null);if(!bodies.ContainsKey(a)||!bodies.ContainsKey(b)||a==b||!active[a].Any()||!active[b].Any())throw new InvalidDataException("Invalid collision pair");var key=PairKey(a,b);if(pairs.ContainsKey(key))throw new InvalidDataException("Duplicate collision pair");pairs[key]=p;foreach(var ga in active[a])foreach(var gb in active[b]){var e=new XElement("pair",new XAttribute("geom1",ga),new XAttribute("geom2",gb));Constraint(e,Effective(config,"contact",Config.D(p,"solver")),true);contact.Add(e);}}
            if(Config.B(rules,"disable_internal",true)){var names=bodies.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray();for(int i=0;i<names.Length;i++)for(int j=i+1;j<names.Length;j++)if(!pairs.ContainsKey(PairKey(names[i],names[j])))contact.Add(new XElement("exclude",new XAttribute("body1",bodies[names[i]].Attribute("name").Value),new XAttribute("body2",bodies[names[j]].Attribute("name").Value)));}
        }
        static string PairKey(string a,string b) { return string.CompareOrdinal(a,b)<0?a.Length+":"+a+b:b.Length+":"+b+a; }
    }
}
