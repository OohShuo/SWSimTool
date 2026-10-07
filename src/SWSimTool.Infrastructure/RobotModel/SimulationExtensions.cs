using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SWSimTool.RobotModel
{
    // Deterministic writer over immutable typed snapshots. Legacy parsing is upstream.
    internal static class SimulationExtensions
    {
        static void Set(XElement e,string key,double value) { e.SetAttributeValue(key,Numbers.Format(value)); }
        static XElement Group(XElement root,string name) { var g=root.Element(name);if(g==null){g=new XElement(name);root.Add(g);}return g; }
        static double Nonnegative(double? input,string key,double fallback) { var v=input??fallback;if(v<0)throw new InvalidDataException("Negative "+key);return v; }
        static void Range(XElement e,string key,double? lower,double? upper) { if(!lower.HasValue||!upper.HasValue||lower>=upper)throw new InvalidDataException("Invalid "+key);e.SetAttributeValue(key,Numbers.Text(new[]{lower.Value,upper.Value})); }
        static void Constraint(XElement e,ConstraintSnapshot c,bool contact,string suffix="")
        {
            if(c==null)return;var t=c.Timeconst??(contact?.003:.005);var d=c.Dampratio??1;var min=c.Dmin??.99;var max=c.Dmax??(contact?.995:.99);var width=c.Width??.001;var midpoint=c.Midpoint??.5;var power=c.Power??2;
            if(t<=0||d<=0||min<=0||max>=1||min>max||width<=0||midpoint<=0||midpoint>=1||power<1)throw new InvalidDataException("Invalid constraint parameters");
            e.SetAttributeValue("solref"+suffix,Numbers.Text(new[]{t,d}));e.SetAttributeValue("solimp"+suffix,Numbers.Text(new[]{min,max,width,midpoint,power}));
            if(contact){Set(e,"margin",Nonnegative(c?.Margin,"margin",.001));var condim=(c?.Condim??3);if(!new[]{1.0,3,4,6}.Contains(condim))throw new InvalidDataException("Invalid condim");Set(e,"condim",condim);}
        }
        static ConstraintSnapshot Effective(SimulationConfigSnapshot config,string kind,ConstraintSnapshot local=null)
        { if(local!=null)return local;return config.Solver?.Enabled==true?(kind=="contact"?config.Solver.Contact??ConstraintSnapshot.Default:config.Solver.Equality??ConstraintSnapshot.Default):null; }
        public static void Apply(XElement root,RobotModel model,SimulationConfigSnapshot config,Dictionary<string,XElement> bodies,Dictionary<string,XElement> joints)
        {
            var settings=config.Joints.ToArray();var actuators=config.Actuators.ToArray();RobotModelValidator.Unique(settings.Select(v=>v.Joint),"joint");RobotModelValidator.Unique(actuators.Select(v=>v.Name),"name");
            var driven=new HashSet<string>(actuators.Select(a=>TypedValues.Text(a?.Joint,null,"joint")));
            if((config.JointDefaults??false))foreach(var pair in joints){var own=settings.SingleOrDefault(s=>TypedValues.Text(s?.Joint,null,"joint")==pair.Key);var defaults=driven.Contains(pair.Key)?new[]{.01,.01,.001}:new[]{.001,.001,0};var keys=new[]{"damping","frictionloss","armature"};for(int i=0;i<3;i++)if(own==null||!(i==0?own.Damping:i==1?own.Frictionloss:own.Armature).HasValue)Set(pair.Value,keys[i],defaults[i]);}
            foreach(var s in settings){var name=TypedValues.Text(s?.Joint,null,"joint");if(!joints.ContainsKey(name))throw new InvalidDataException("Unknown scalar joint: "+name);var j=joints[name];var type=TypedValues.Text(s?.Type,"inherit","type");var limit=TypedValues.Text(s?.LimitMode,"inherit","limit_mode");if(!new[]{"inherit","none","custom"}.Contains(limit))throw new InvalidDataException("Invalid limit mode");
                if(type!="inherit"){if(type!="hinge"&&type!="slide")throw new InvalidDataException("Invalid joint override");if(type!=(string)j.Attribute("type")&&limit=="inherit")throw new InvalidDataException("Changing joint type requires explicit limits");j.SetAttributeValue("type",type);}
                foreach(var value in new[]{new KeyValuePair<string,double?>("damping",s.Damping),new KeyValuePair<string,double?>("frictionloss",s.Frictionloss),new KeyValuePair<string,double?>("armature",s.Armature),new KeyValuePair<string,double?>("stiffness",s.Stiffness),new KeyValuePair<string,double?>("springref",s.Springref),new KeyValuePair<string,double?>("ref",s.Ref),new KeyValuePair<string,double?>("margin",s.Margin)})if(value.Value.HasValue)Set(j,value.Key,value.Key=="springref"||value.Key=="ref"?value.Value.Value:Nonnegative(value.Value,value.Key,0));
                if(limit=="none"){j.SetAttributeValue("limited","false");j.Attribute("range")?.Remove();}else if(limit=="custom"){j.SetAttributeValue("limited","true");Range(j,"range",s.Lower,s.Upper);}
                Constraint(j,s?.LimitSolver,false,"limit");Constraint(j,s?.FrictionSolver,false,"friction");
                if(s.Pos!=null||s.Axis!=null)throw new NotSupportedException("Joint position/axis override is not part of candidate model");
            }
            var forces=config.JointForceLimits.ToArray();RobotModelValidator.Unique(forces.Select(v=>v.Joint),"joint");foreach(var f in forces){var name=TypedValues.Text(f?.Joint,null,"joint");if(!joints.ContainsKey(name))throw new InvalidDataException("Unknown force-limited joint");var j=joints[name];j.SetAttributeValue("actuatorfrclimited","true");Range(j,"actuatorfrcrange",f.Lower,f.Upper);}
            var mode=TypedValues.Text(config.BaseMode,"inherit","base_mode");if(!new[]{"inherit","fixed","floating"}.Contains(mode))throw new InvalidDataException("Invalid base mode");if(mode=="floating"){
                var body=bodies[model.Core.Root.Id];if(body.Name!="body")throw new InvalidDataException("Missing floating root body");if(root.Descendants().Any(e=>(string)e.Attribute("name")=="__swsimtool_base_free"))throw new InvalidDataException("Reserved free joint name");body.AddFirst(new XElement("freejoint",new XAttribute("name","__swsimtool_base_free")));
            }
            var solver=config.Solver;var enabled=(solver?.Enabled??false);
            var overrides=settings.Any(s=>s?.LimitSolver!=null||s?.FrictionSolver!=null)||config.Equalities.Any(e=>e?.Solver!=null)||(config.Collision?.AllowedPairs??Array.AsReadOnly(new ContactPairSnapshot[0])).Any(p=>p?.Solver!=null);
            if(enabled||overrides){var option=Group(root,"option");option.Add(new XElement("flag",new XAttribute("refsafe","enable")));if(enabled){option.SetAttributeValue("solver","Newton");option.SetAttributeValue("cone","elliptic");var dt=(solver?.Timestep??.001);var iter=(solver?.Iterations??100);var no=(solver?.NoslipIterations??0);var ratio=(solver?.Impratio??10);if(dt<=0||iter<1||iter!=Math.Floor(iter)||no<0||no!=Math.Floor(no)||ratio<=0)throw new InvalidDataException("Invalid solver settings");Set(option,"timestep",dt);Set(option,"iterations",iter);Set(option,"noslip_iterations",no);Set(option,"impratio",ratio);Set(option,"tolerance",Nonnegative(solver?.Tolerance,"tolerance",1e-9));foreach(var g in root.Descendants("geom"))if((string)g.Attribute("contype")!="0"||(string)g.Attribute("conaffinity")!="0")Constraint(g,Effective(config,"contact"),true);}}
            Collision(root,model,config,bodies);
            foreach(var a in actuators){var type=TypedValues.Text(a?.Type,null,"type");if(!new[]{"motor","position","velocity"}.Contains(type))throw new NotSupportedException("Actuator type: "+type);var joint=TypedValues.Text(a?.Joint,null,"joint");if(!joints.ContainsKey(joint))throw new InvalidDataException("Unknown actuator joint");var e=new XElement(type,new XAttribute("name",TypedValues.Text(a?.Name,null,"name")),new XAttribute("joint",joints[joint].Attribute("name").Value),new XAttribute("ctrllimited","true"),new XAttribute("forcelimited","true"));Set(e,"gear",(a?.Gear??1));Range(e,"ctrlrange",a.CtrlMin,a.CtrlMax);Range(e,"forcerange",a.ForceMin,a.ForceMax);if(type!="motor")Set(e,type=="position"?"kp":"kv",Nonnegative(a?.Gain,"gain",1));Group(root,"actuator").Add(e);}
            Sensors(root,model,config,bodies);
            Equalities(root,model,config,bodies,joints);
            SiteForces(root,model,config);
            // Names are unique within each MuJoCo object namespace, including generated names.
            foreach(var kind in new[]{"body","joint","geom","site","camera"})RobotModelValidator.Unique(root.Descendants(kind).Where(e=>e.Attribute("name")!=null).Select(e=>(string)e.Attribute("name")),kind+" name");
            foreach(var kind in new[]{"actuator","sensor","equality","tendon"})RobotModelValidator.Unique(root.Elements(kind).Elements().Where(e=>e.Attribute("name")!=null).Select(e=>(string)e.Attribute("name")),kind+" name");
        }
        static void Sensors(XElement root,RobotModel model,SimulationConfigSnapshot config,Dictionary<string,XElement> bodies)
        {
            var sensors=config.Sensors.ToArray();RobotModelValidator.Unique(sensors.Select(v=>v.Name),"name");
            foreach(var s in sensors){var name=TypedValues.Text(s?.Name,null,"name");var site=FindSite(model,s.Site,s.SiteId);var type=TypedValues.Text(s?.Type,null,"type");if(!site.IsFrame)throw new InvalidDataException("Sensor requires an oriented frame");if(type=="camera"){var fovy=(s?.Fovy??45);if(fovy<=0||fovy>=180)throw new InvalidDataException("Invalid camera field of view");var camera=new XElement("camera",new XAttribute("name",name),new XAttribute("fovy",Numbers.Format(fovy)));MjcfExporter.Pose(camera,site.LinkFromSite);bodies[site.LinkId].Add(camera);}
                else{if(type!="imu"&&type!="tof")throw new NotSupportedException("Sensor type: "+type);foreach(var kind in type=="imu"?new[]{"accelerometer","gyro"}:new[]{"rangefinder"})Group(root,"sensor").Add(new XElement(kind,new XAttribute("name",name+"_"+kind),new XAttribute("site",site.Name),new XAttribute("cutoff",Numbers.Format(Nonnegative(s?.Cutoff,"cutoff",0)))));}}
        }
        static SiteSnapshot FindSite(RobotModel model,string name,string id)
        {
            var site=!string.IsNullOrEmpty(id)?model.Simulation.Sites.SingleOrDefault(s=>s.Id==id):model.Simulation.Sites.SingleOrDefault(s=>s.Name==name);if(site==null)throw new InvalidDataException("Unknown site: "+name);return site;
        }
        static void Equalities(XElement root,RobotModel model,SimulationConfigSnapshot config,Dictionary<string,XElement> bodies,Dictionary<string,XElement> joints)
        {
            var values=config.Equalities.ToArray();RobotModelValidator.Unique(values.Select(v=>v.Name),"name");
            foreach(var v in values){var type=TypedValues.Text(v?.Type,null,"type");if(!new[]{"connect","weld","joint"}.Contains(type))throw new NotSupportedException("Equality type");var e=new XElement(type,new XAttribute("name",TypedValues.Text(v?.Name,null,"name")),new XAttribute("active",(v?.Active??true)?"true":"false"));Constraint(e,Effective(config,"equality",v?.Solver),false);
                if(type=="joint"){var a=TypedValues.Text(v?.Joint1,null,"joint1");var b=TypedValues.Text(v?.Joint2,"","joint2");if(!joints.ContainsKey(a)||(b!=""&&(!joints.ContainsKey(b)||a==b)))throw new InvalidDataException("Invalid equality joint");e.SetAttributeValue("joint1",joints[a].Attribute("name").Value);if(b!="")e.SetAttributeValue("joint2",joints[b].Attribute("name").Value);e.SetAttributeValue("polycoef",Numbers.Text(TypedValues.Vector(v?.Polycoef,5,new[]{0.0,1,0,0,0},"polycoef")));}
                else{var binding=TypedValues.Text(v?.Binding,"site","binding");if(binding=="site"){var a=FindSite(model,v.Site1,v.Site1Id);var b=FindSite(model,v.Site2,v.Site2Id);if(a.Id==b.Id||a.LinkId==b.LinkId||(type=="weld"&&(!a.IsFrame||!b.IsFrame)))throw new InvalidDataException("Invalid equality sites");e.SetAttributeValue("site1",a.Name);e.SetAttributeValue("site2",b.Name);}
                    else if(binding=="body"){var a=TypedValues.Text(v?.Body1,null,"body1");var b=TypedValues.Text(v?.Body2,"","body2");if(!bodies.ContainsKey(a)||(b!=""&&(!bodies.ContainsKey(b)||a==b)))throw new InvalidDataException("Invalid equality bodies");e.SetAttributeValue("body1",bodies[a].Attribute("name").Value);if(b!="")e.SetAttributeValue("body2",bodies[b].Attribute("name").Value);
                        if(type=="connect")e.SetAttributeValue("anchor",Numbers.Text(TypedValues.Vector(v?.Anchor,3,new double[3],"anchor")));else{var pose=TypedValues.Text(v?.PoseMode,"inherit","pose_mode");if(pose=="custom"){var p=TypedValues.Vector(v?.Position,3,null,"position");var r=TypedValues.Vector(v?.Orientation,3,null,"orientation");e.SetAttributeValue("relpose",Numbers.Text(p)+" "+Quaterniond.FromRpy(new Vector3d(r[0],r[1],r[2])).ToString());}else if(pose!="inherit")throw new InvalidDataException("Invalid weld pose mode");}}
                    else throw new InvalidDataException("Invalid equality binding");if(type=="weld")Set(e,"torquescale",Nonnegative(v?.Torquescale,"torquescale",1));}
                Group(root,"equality").Add(e);
            }
        }
        static void SiteForces(XElement root,RobotModel model,SimulationConfigSnapshot config)
        {
            var values=config.SiteForces.ToArray();RobotModelValidator.Unique(values.Select(v=>v.Name),"name");var world=new Dictionary<string,RigidTransform>();world[model.Core.Root.Id]=RigidTransform.Identity;Action<string> visit=null;visit=id=>{foreach(var j in model.Core.Joints.Where(j=>j.ParentLinkId==id)){world[j.ChildLinkId]=world[id]*j.ParentLinkFromJoint;visit(j.ChildLinkId);}};visit(model.Core.Root.Id);
            foreach(var v in values){var name=TypedValues.Text(v?.Name,null,"name");var type=TypedValues.Text(v?.Type,"pull","type");if(!new[]{"pull","push","spring"}.Contains(type))throw new NotSupportedException("Two-site force type");var a=FindSite(model,v.Site1,v.Site1Id);var b=FindSite(model,v.Site2,v.Site2Id);if(a.Id==b.Id||a.LinkId==b.LinkId)throw new InvalidDataException("Invalid force endpoints");var k=Nonnegative(v?.Stiffness,"stiffness",0);var damping=Nonnegative(v?.Damping,"damping",0);var magnitude=Nonnegative(v?.Magnitude,"magnitude",0);if(!(v?.Enabled??true))continue;
                var gap=(world[a.LinkId].Apply(a.LinkFromSite.Translation)-world[b.LinkId].Apply(b.LinkFromSite.Translation)).Length;if((type=="spring"?(k>0||damping>0):magnitude>0)&&gap<=1e-9)throw new InvalidDataException("Two-site force endpoints coincide initially");
                var path=new XElement("spatial",new XAttribute("name","swsimtool_tendon_"+name),new XAttribute("limited","false"),new XAttribute("stiffness","0"),new XAttribute("damping","0"),new XAttribute("frictionloss","0"),new XAttribute("width",".001"),new XElement("site",new XAttribute("site",a.Name)),new XElement("site",new XAttribute("site",b.Name)));
                if(type=="spring"){Set(path,"stiffness",k);Set(path,"damping",damping);var lengthMode=TypedValues.Text(v?.LengthMode,"initial","length_mode");if(lengthMode!="custom"&&lengthMode!="initial")throw new InvalidDataException("Invalid spring length mode");Set(path,"springlength",lengthMode=="custom"?Nonnegative(v?.RestLength,"rest_length",.1):gap);}
                else Group(root,"actuator").Add(new XElement("general",new XAttribute("name","swsimtool_force_"+name),new XAttribute("tendon","swsimtool_tendon_"+name),new XAttribute("gear","1"),new XAttribute("dyntype","none"),new XAttribute("gaintype","fixed"),new XAttribute("gainprm","0"),new XAttribute("biastype","affine"),new XAttribute("biasprm",Numbers.Format(type=="pull"?-magnitude:magnitude)+" 0 0"),new XAttribute("ctrllimited","false"),new XAttribute("forcelimited","false")));
                Group(root,"tendon").Add(path);
            }
        }
        static void Collision(XElement root,RobotModel model,SimulationConfigSnapshot config,Dictionary<string,XElement> bodies)
        {
            var rules=config.Collision;if(rules==null)return;var modes=rules.LinkModes;foreach(var name in modes.Keys)if(!bodies.ContainsKey(name))throw new InvalidDataException("Unknown collision link");
            var active=new Dictionary<string,List<string>>();foreach(var b in bodies){var mode=(modes.ContainsKey(b.Key)?modes[b.Key]:"mesh");if(!new[]{"mesh","primitive","none"}.Contains(mode))throw new InvalidDataException("Invalid collision mode");active[b.Key]=new List<string>();foreach(var g in b.Value.Elements("geom")){if(mode!="mesh"){g.SetAttributeValue("contype","0");g.SetAttributeValue("conaffinity","0");}else if((string)g.Attribute("contype")!="0"||(string)g.Attribute("conaffinity")!="0")active[b.Key].Add(g.Attribute("name").Value);}}
            var geometries=rules?.Geometries.ToArray();RobotModelValidator.Unique(geometries.Select(v=>v.Name),"name");foreach(var v in geometries){var link=TypedValues.Text(v?.Link,null,"link");if(!bodies.ContainsKey(link))throw new InvalidDataException("Unknown collision geometry link");var type=TypedValues.Text(v?.Type,null,"type");var length=type=="box"?3:type=="sphere"?1:type=="cylinder"||type=="capsule"?2:0;if(length==0)throw new NotSupportedException("Collision geometry type");var size=TypedValues.Vector(v?.Size,length,null,"size");if(size.Any(x=>x<=0))throw new InvalidDataException("Collision size must be positive");var xyz=TypedValues.Vector(v?.Xyz,3,null,"xyz");var rpy=TypedValues.Vector(v?.Rpy,3,null,"rpy");if((modes.ContainsKey(link)?modes[link]:"mesh")!="primitive")continue;if(type=="box")size=size.Select(x=>x/2).ToArray();else if(length==2)size[1]/=2;
                var g=new XElement("geom",new XAttribute("name",TypedValues.Text(v?.Name,null,"name")),new XAttribute("type",type),new XAttribute("size",Numbers.Text(size)),new XAttribute("group","3"),new XAttribute("mass","0"),new XAttribute("contype","1"),new XAttribute("conaffinity","1"),new XAttribute("rgba","0.2 0.8 1 0.35"));MjcfExporter.Pose(g,new RigidTransform(new Vector3d(xyz[0],xyz[1],xyz[2]),Quaterniond.FromRpy(new Vector3d(rpy[0],rpy[1],rpy[2]))));Constraint(g,Effective(config,"contact"),true);bodies[link].Add(g);active[link].Add(g.Attribute("name").Value);
            }
            foreach(var name in bodies.Keys)if((modes.ContainsKey(name)?modes[name]:"mesh")=="primitive"&&!active[name].Any())throw new InvalidDataException("Primitive mode has no geometry");var contact=Group(root,"contact");var pairs=new Dictionary<string,ContactPairSnapshot>();
            foreach(var p in rules?.AllowedPairs){var a=TypedValues.Text(p?.Link1,null,"link1");var b=TypedValues.Text(p?.Link2,null,"link2");if(!bodies.ContainsKey(a)||!bodies.ContainsKey(b)||a==b||!active[a].Any()||!active[b].Any())throw new InvalidDataException("Invalid collision pair");var key=PairKey(a,b);if(pairs.ContainsKey(key))throw new InvalidDataException("Duplicate collision pair");pairs[key]=p;foreach(var ga in active[a])foreach(var gb in active[b]){var e=new XElement("pair",new XAttribute("geom1",ga),new XAttribute("geom2",gb));Constraint(e,Effective(config,"contact",p?.Solver),true);contact.Add(e);}}
            if((rules?.DisableInternal??true)){var names=bodies.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray();for(int i=0;i<names.Length;i++)for(int j=i+1;j<names.Length;j++)if(!pairs.ContainsKey(PairKey(names[i],names[j])))contact.Add(new XElement("exclude",new XAttribute("body1",bodies[names[i]].Attribute("name").Value),new XAttribute("body2",bodies[names[j]].Attribute("name").Value)));}
        }
        static string PairKey(string a,string b) { return string.CompareOrdinal(a,b)<0?a.Length+":"+a+b:b.Length+":"+b+a; }
    }
}
