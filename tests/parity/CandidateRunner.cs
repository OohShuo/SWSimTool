using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SWSimTool.RobotModel;

public static class CandidateRunner
{
    public static int Main(string[] args)
    {
        try {
            if(args.Length==3&&args[0]=="--tooltest"){MuJoCoSettingsTests.Run();ToolBackendTests.Run(args[1],args[2]);return 0;}
            if(args.Length==1&&args[0]=="--selftest"){SelfTest();return 0;}
            if(args.Length==6&&args[0]=="--package"){
                var packageModel=SWSimTool.Simulation.NativeBackend.LoadLocal(args[2],args[3]);
                return SWSimTool.Simulation.NativeBackend.RunAsync(args[1],packageModel,args[4],false,Console.WriteLine,args[5],"package-test").GetAwaiter().GetResult();
            }
            if(args.Length!=3)throw new ArgumentException("Usage: candidate.exe input.urdf config.json output.xml");
            var core=UrdfRobotModelImporter.Load(args[0]);var simulation=LegacySimulationConfigImporter.Import(File.ReadAllText(args[1]),core);
            var meshes=core.Links.SelectMany(l=>l.Geometries).Where(g=>g.Mesh!=null).Select(g=>g.Mesh).GroupBy(m=>m.Id).Select(g=>g.First()).ToArray();
            var output=Path.GetFullPath(args[2]);var folder=Path.GetDirectoryName(output);Directory.CreateDirectory(folder);
            var prepared=new List<PreparedMeshAsset>();for(int i=0;i<meshes.Length;i++){
                if(!File.Exists(meshes[i].SourcePath))throw new FileNotFoundException("Mesh source missing",meshes[i].SourcePath);
                // Fixture assets only; production preparation is a separate service with triangle-budget validation.
                var relative="meshes/mesh_"+i.ToString("D4")+Path.GetExtension(meshes[i].SourcePath).ToLowerInvariant();prepared.Add(new PreparedMeshAsset(meshes[i].Id,"mesh_"+i,relative));
            }
            var xml=MjcfExporter.Generate(new RobotModel(core,simulation),new PreparedAssets(prepared),new ExportContext(core.Name));
            for(int i=0;i<meshes.Length;i++){var target=Path.Combine(folder,prepared[i].RelativePath);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(meshes[i].SourcePath,target,true);}
            File.WriteAllText(output,xml,new UTF8Encoding(false));return 0;
        }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}
    }
    static void Check(bool value,string label) { if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label); }
    static void SelfTest()
    {
        var rotated=Quaterniond.FromRpy(new Vector3d(0,0,Math.PI/2)).Rotate(new Vector3d(1,0,0));Check(Math.Abs(rotated.X)<1e-14&&Math.Abs(rotated.Y-1)<1e-14,"double rotation convention");
        var inertia=new SymmetricInertia(1,2,2.5,0,0,0).Rotated(Quaterniond.FromRpy(new Vector3d(0,0,Math.PI/2)));Check(Math.Abs(inertia.XX-2)<1e-14&&Math.Abs(inertia.YY-1)<1e-14,"inertia rotated into link frame");
        var geometry=new[]{new GeometrySnapshot("g",GeometryKind.Box,RigidTransform.Identity,new Vector3d(1,2,3),null,true,new[]{.2,.3,.4,1})};var links=new List<LinkSnapshot>{new LinkSnapshot("base","base",null,geometry)};
        var core=new RobotCoreSnapshot("test",links,new JointSnapshot[0]);links.Clear();geometry[0]=null;var returned=core.Links[0].Geometries[0].Rgba;returned[0]=99;Check(core.Links.Count==1&&core.Links[0].Geometries[0].Rgba[0]==.2,"snapshots own collections and color data");
        var first=new RobotModel(core,LegacySimulationConfigImporter.Import("{\"solver\":{\"enabled\":true,\"timestep\":0.001}}"));var second=new RobotModel(core,LegacySimulationConfigImporter.Import("{\"solver\":{\"enabled\":true,\"timestep\":0.002}}"));var assets=new PreparedAssets(new PreparedMeshAsset[0]);var context=new ExportContext("test");var before=MjcfExporter.Generate(first,assets,context);var changed=MjcfExporter.Generate(second,assets,context);Check(Object.ReferenceEquals(first.Core,second.Core)&&before==MjcfExporter.Generate(first,assets,context)&&before!=changed,"solver rebuild shares core without changing old model");
        bool failed=false;try{new JointSnapshot("j","j","base","arm",JointKind.Continuous,RigidTransform.Identity,new Vector3d(),null,null,0,0);}catch(InvalidDataException){failed=true;}Check(failed,"zero axis rejected");
        failed=false;try{new SymmetricInertia(1,1,4,0,0,0).Validate(1);}catch(InvalidDataException){failed=true;}Check(failed,"unphysical inertia triangle rejected");
        failed=false;try{new PreparedMeshAsset("x","x","../escape.stl");}catch(InvalidDataException){failed=true;}Check(failed,"prepared asset traversal rejected");
        var stableCore=new RobotCoreSnapshot("stable",new[]{new LinkSnapshot("link-uuid","renamed",null,new GeometrySnapshot[0])},new JointSnapshot[0]);
        var stableSim=LegacySimulationConfigImporter.Import("{\"attachments\":[{\"id\":\"site-uuid\",\"name\":\"tip\",\"link\":\"old_name\",\"link_id\":\"link-uuid\",\"type\":\"frame\",\"xyz\":[0,0,0],\"rpy\":[0,0,0]}]}",stableCore);
        Check(MjcfExporter.Generate(new RobotModel(stableCore,stableSim),assets,context).Contains("renamed"),"stable references survive names independent of IDs");
        failed=false;try{LegacySimulationConfigImporter.Import("{\"attachments\":[{\"name\":\"tip\",\"link\":\"renamed\",\"link_id\":\"deleted-id\",\"type\":\"frame\",\"xyz\":[0,0,0],\"rpy\":[0,0,0]}]}",stableCore);}catch(InvalidDataException){failed=true;}Check(failed,"deleted identity never rebinds by name");
        var frozen=LegacySimulationConfigImporter.Import("{\"equalities\":[{\"name\":\"couple\",\"type\":\"joint\",\"polycoef\":[0,1,0,0,0]}]}");
        failed=false;try{((IList<double>)frozen.Equalities[0].Polycoef)[1]=4;}catch(NotSupportedException){failed=true;}Check(failed,"typed extension vectors are immutable");
        var owned=Path.Combine(Path.GetTempPath(),"SWSimTool-publish-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);
        try {
            var prior=Path.Combine(owned,"test_mjcf");var staging=Path.Combine(owned,".stage");Directory.CreateDirectory(prior);Directory.CreateDirectory(staging);
            var target=Path.Combine(prior,"test.xml");var oldXml="<mujoco model=\"old\"><worldbody/></mujoco>";File.WriteAllText(target,oldXml);File.WriteAllText(Path.Combine(staging,"test.xml"),"<mujoco model=\"new\"><worldbody/></mujoco>");
            int operations=0;failed=false;try{SWSimTool.Simulation.PackagePublisher.Publish(staging,target,(a,b)=>{if(++operations==2)throw new IOException("Injected publish failure");Directory.Move(a,b);});}catch(IOException){failed=true;}
            Check(failed&&operations==3&&File.ReadAllText(target)==oldXml,"failed package swap restores previous complete package");
        }finally{Directory.Delete(owned,true);}
    }
}
