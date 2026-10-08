using System;
using System.Linq;
using SWSimTool.Simulation;
public static class ConfigurationDependencyTests {
    static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    public static void Run(){
        var p=new SimulationProject();
        var a=new Attachment{name="A",link="arm",link_id="link-A"};
        var b=new Attachment{name="B",link="other",link_id="link-B"};p.attachments.Add(a);p.attachments.Add(b);
        p.sensors.Add(new SensorConfig{name="imu",site="A",site_id=a.id});
        p.site_forces.Add(new SiteForceConfig{name="force",site1="A",site1_id=a.id,site2="B",site2_id=b.id});
        p.site_forces.Add(new SiteForceConfig{name="spring",type="spring",site1="A",site1_id=a.id,site2="B",site2_id=b.id});
        p.equalities.Add(new EqualityConfig{name="connect",site1="A",site1_id=a.id,site2="B",site2_id=b.id});
        p.actuators.Add(new ActuatorConfig{name="motor",joint="hinge",joint_id="joint-A"});
        p.joints.Add(new JointConfiguration{joint="hinge",joint_id="joint-A"});
        p.joint_force_limits.Add(new JointForceLimit{joint="hinge",joint_id="joint-A"});
        p.collision=new CollisionConfiguration();p.collision.geometries.Add(new CollisionGeometry{name="box",link="arm",link_id="link-A"});
        p.collision.allowed_pairs.Add(new CollisionPair{link1="arm",link1_id="link-A",link2="other",link2_id="link-B"});p.collision.link_modes_by_id.Add("link-A","primitive");
        string before=ExportFingerprint.Hash(p);
        var sitePlan=ConfigurationDependencies.Plan(p,sites:new[]{a.id});
        Check(before==ExportFingerprint.Hash(p),"planning/cancel deletion leaves complete draft unchanged");
        Check(sitePlan.Result.attachments.Single().id==b.id&&sitePlan.Result.sensors.Count==0&&sitePlan.Result.equalities.Count==0&&sitePlan.Result.site_forces.Count==0,"site deletion cleans sensors, connect, constant force and spring");
        var plan=ConfigurationDependencies.Plan(p,links:new[]{"link-A"},joints:new[]{"joint-A"});
        Check(plan.Result.actuators.Count==0&&plan.Result.joints.Count==0&&plan.Result.joint_force_limits.Count==0&&plan.Result.collision.geometries.Count==0&&plan.Result.collision.allowed_pairs.Count==0&&plan.Result.collision.link_modes_by_id.Count==0,"subtree deletion removes joint and collision dependencies");
        plan.ApplyTo(p);Check(p.attachments.Single().id==b.id&&p.site_forces.Count==0,"confirmed deletion applies complete validated result");
        p.attachments.Add(new Attachment{name="B"});before=ExportFingerprint.Hash(p);bool failed=false;
        try{ConfigurationDependencies.Plan(p,sites:new[]{b.id});}catch(System.IO.InvalidDataException){failed=true;}
        Check(failed&&before==ExportFingerprint.Hash(p),"validation failure cannot partially delete configuration");
    }
}
