using System;
using System.IO;
using System.Xml;
using SWSimTool.URDF;

public static class TestRootJointSerialization {
    public static void Main() {
        var root = new Link(null) { Name = "base" };
        root.Joint.Name = "stale_joint";
        root.Joint.Type = "fixed";
        root.Joint.Parent.Name = "old_parent";
        root.Joint.Child.Name = "removed_link";
        var child = new Link(root) { Name = "arm" };
        child.Joint.Name = "arm_joint";
        child.Joint.Type = "fixed";
        child.Joint.Parent.Name = "base";
        child.Joint.Child.Name = "arm";
        root.Children.Add(child);
        var robot = new Robot { Name = "test" };
        robot.SetBaseLink(root);
        var text = new StringWriter();
        robot.WriteURDF(XmlWriter.Create(text));
        var xml = new XmlDocument(); xml.LoadXml(text.ToString());
        if(xml.SelectNodes("/robot/joint").Count != 1 || xml.SelectSingleNode("/robot/joint[@name='arm_joint']") == null)
            throw new Exception("Root stale joint exported or child joint lost");
        if(root.Joint.Name != "stale_joint") throw new Exception("Saved configuration mutated");
        Console.WriteLine("PASS: omit stale root joint, retain child joint, preserve configuration");
    }
}
