using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace SW2URDF.Simulation
{
    // One visible Attribute, with a versioned map keyed by SW Configuration name.
    public static class SimulationStorage
    {
        public const string NodeName = "MuJoCo Simulation Configuration (v1)";
        public sealed class Document
        {
            public int version { get; set; } = 1;
            public Dictionary<string, SimulationProject> configurations { get; set; } = new Dictionary<string, SimulationProject>();
        }
        static SolidWorks.Interop.sldworks.Attribute Find(ModelDoc2 model)
        {
            foreach (Feature f in (object[])model.FeatureManager.GetFeatures(true) ?? new object[0])
                if (f.GetTypeName2() == "Attribute")
                {
                    var a = f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;
                    if (a != null && a.GetName() == NodeName) return a;
                }
            return null;
        }
        static Document Read(ModelDoc2 model)
        {
            var a = Find(model);
            if (a == null) return new Document();
            var data = ((Parameter)a.GetParameter("data")).GetStringValue();
            var value = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<Document>(data);
            if (value == null || value.version != 1 || value.configurations == null) throw new InvalidDataException("装配内仿真配置损坏或版本不支持。");
            return value;
        }
        public static SimulationProject Load(ModelDoc2 model)
        {
            var document = Read(model);
            SimulationProject value;
            return document.configurations.TryGetValue(model.ConfigurationManager.ActiveConfiguration.Name, out value) ? value : null;
        }
        public static void Save(SldWorks app, ModelDoc2 model, SimulationProject project)
        {
            var document = Read(model);
            document.configurations[model.ConfigurationManager.ActiveConfiguration.Name] = project;
            var data = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(document);
            var a = Find(model);
            if (a == null)
            {
                AttributeDef def = (AttributeDef)app.DefineAttribute(NodeName);
                def.AddParameter("data", (int)swParamType_e.swParamTypeString, 0, 0);
                def.Register();
                a = def.CreateInstance5(model, null, NodeName, 0, (int)swInConfigurationOpts_e.swAllConfiguration);
                if (a == null) throw new InvalidOperationException("无法创建仿真配置节点。");
            }
            if (!((Parameter)a.GetParameter("data")).SetStringValue2(data, (int)swInConfigurationOpts_e.swAllConfiguration, ""))
                throw new IOException("无法保存装配内仿真配置。");
            model.SetSaveFlag();
        }
    }
}
