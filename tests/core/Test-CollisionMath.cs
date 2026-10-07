using System;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using MathNet.Numerics.LinearAlgebra;
using SW2URDF.Simulation;
using SW2URDF.Utilities;

public static class CollisionMathTests
{
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
    public static void Main()
    {
        var method=typeof(AttachmentService).GetMethod("AxisFrame",BindingFlags.NonPublic|BindingFlags.Static);
        foreach(var direction in new[]{new[]{3.0,4,12},new[]{1.0,0,0},new[]{0.0,0,-1},new[]{0.0,1,0}})
        {
            var frame=(Matrix<double>)method.Invoke(null,new object[]{new[]{.1,.2,.3},direction});
            var rotation=frame.SubMatrix(0,3,0,3);
            Check((rotation.Transpose()*rotation-MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.CreateIdentity(3)).L2Norm()<1e-12,"Axis frame must be orthonormal");
            Check(Math.Abs(rotation.Determinant()-1)<1e-12,"Axis frame must be right handed");
            Check((MathOps.GetTransformation(MathOps.GetXYZ(frame),MathOps.GetRPY(frame))-frame).L2Norm()<1e-12,"RPY reconstruction must preserve orientation");
        }
        var project=new SimulationProject{collision=new CollisionConfiguration()};
        var g=new CollisionGeometry{name="capsule",link="arm",type="capsule",size=new[]{.01,.05},definition="endpoints"};
        g.references.Add(new CollisionReference{pid="AQID",label="first"});
        project.collision.geometries.Add(g);
        var serializer=new JavaScriptSerializer();
        var doc=new SimulationStorage.Document();doc.configurations["A"]=new SimulationStorage.Entry{simulation=project};doc.configurations["B"]=new SimulationStorage.Entry{simulation=new SimulationProject()};
        var restored=serializer.Deserialize<SimulationStorage.Document>(serializer.Serialize(doc));
        Check(restored.configurations["A"].simulation.collision.geometries[0].references[0].pid=="AQID","Geometry reference persistence");
        Check(restored.configurations["B"].simulation.collision==null,"Configuration isolation / legacy nullable collision");
        g.Validate();
        g.size[0]=double.NaN;
        bool rejected=false;try{g.Validate();}catch(Exception error){if(!(error is InvalidOperationException)&&!(error is System.IO.InvalidDataException))throw;rejected=true;}
        Check(rejected,"Reject non-finite geometry dimensions");
        Console.WriteLine("PASS: coordinate frames, RPY reconstruction, per-configuration JSON persistence and invalid geometry rejection");
    }
}
