using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SWSimTool.URDF
{
    [DataContract(Namespace = SWSimTool.Persistence.DocumentStorageSchema.JointCadReferenceNamespace)]
    public sealed class JointCadReference
    {
        [DataMember] public string FeatureId { get; set; }
        [DataMember] public string ComponentId { get; set; }
        [DataMember] public string Name { get; set; }
    }
    //The joint class. There is one for every link but the base link
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Joint : URDFElement
    {
        [DataMember(IsRequired = false)]
        private string stableId=System.Guid.NewGuid().ToString("N");
        internal string ExistingStableId=>stableId;
        public string StableId { get { return stableId ?? (stableId = System.Guid.NewGuid().ToString("N")); } }
        public static readonly List<string> AvailableTypes = new List<string>
        {
            "revolute", "continuous", "prismatic", "fixed", "floating", "planar"
        };

        [DataMember]
        private readonly URDFAttribute NameAttribute;

        public string Name
        {
            get => (string)NameAttribute.Value;
            set => NameAttribute.Value = value;
        }

        [DataMember]
        private readonly URDFAttribute TypeAttribute;

        public string Type
        {
            get => (string)TypeAttribute.Value;
            set => TypeAttribute.Value = value;
        }

        [DataMember]
        public readonly Origin Origin;

        [DataMember]
        public readonly ParentLink Parent;

        [DataMember]
        public readonly ChildLink Child;

        [DataMember]
        public readonly Axis Axis;

        [DataMember]
        public readonly Limit Limit;

        [DataMember]
        public readonly Calibration Calibration;

        [DataMember]
        public readonly Dynamics Dynamics;

        [DataMember]
        public readonly SafetyController Safety;

        [DataMember(IsRequired = false)]
        public readonly Mimic Mimic;

        private string coordinateSystemName,axisName;
        [DataMember(IsRequired=false)] public JointCadReference CoordinateReference;
        [DataMember(IsRequired=false)] public JointCadReference AxisReference;
        [DataMember]
        public string CoordinateSystemName {get=>coordinateSystemName;set{if(coordinateSystemName!=null&&coordinateSystemName!=value)CoordinateReference=null;coordinateSystemName=value;}}
        [DataMember]
        public string AxisName {get=>axisName;set{if(axisName!=null&&axisName!=value)AxisReference=null;axisName=value;}}
        public void BindCoordinate(JointCadReference reference){CoordinateReference=reference;coordinateSystemName=reference.Name;}
        public void BindAxis(JointCadReference reference){AxisReference=reference;axisName=reference.Name;}
        static JointCadReference CopyReference(JointCadReference reference)=>reference==null?null:new JointCadReference{FeatureId=reference.FeatureId,ComponentId=reference.ComponentId,Name=reference.Name};

        public Joint() : base("joint", false)
        {
            Origin = new Origin(false);
            Parent = new ParentLink();
            Child = new ChildLink();
            Axis = new Axis();

            Limit = new Limit();
            Calibration = new Calibration();
            Dynamics = new Dynamics();
            Safety = new SafetyController();
            Mimic = new Mimic();

            NameAttribute = new URDFAttribute("name", true, "");
            TypeAttribute = new URDFAttribute("type", true, "");

            Attributes.Add(NameAttribute);
            Attributes.Add(TypeAttribute);

            ChildElements.Add(Origin);
            ChildElements.Add(Parent);
            ChildElements.Add(Child);
            ChildElements.Add(Axis);

            ChildElements.Add(Limit);
            ChildElements.Add(Calibration);
            ChildElements.Add(Dynamics);
            ChildElements.Add(Safety);
            ChildElements.Add(Mimic);
        }

        public void FillBoxes(TextBox boxName, ComboBox boxType)
        {
            boxName.Text = Name;
            boxType.Text = Type;
        }

        public void Update(TextBox boxName, ComboBox boxType)
        {
            Name = boxName.Text;
            Type = boxType.Text;
        }

        public override bool ElementContainsData()
        {
            return !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Type);
        }

        public override void WriteURDF(System.Xml.XmlWriter writer){
            var copy=new Joint();copy.SetElement(this);
            if(Type=="fixed"||Type=="floating"||Type=="planar")copy.Limit.Unset();
            else if(Type=="continuous")copy.Limit.ClearBounds();
            copy.WriteForType(writer);
        }
        bool ValidateForType()=>base.AreRequiredFieldsSatisfied();
        void WriteForType(System.Xml.XmlWriter writer){base.WriteURDF(writer);}
        public override bool AreRequiredFieldsSatisfied()
        {
            var copy=new Joint();copy.SetElement(this);
            if(Type=="fixed"||Type=="floating"||Type=="planar")copy.Limit.Unset();
            else if(Type=="continuous")copy.Limit.ClearBounds();
            copy.Limit.SetRequired(Type=="prismatic"||Type=="revolute");
            return copy.ValidateForType();
        }

        public override void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            string contextString = string.Join(".", context);

            string coordSysContext = contextString + ".CoordSysName";
            dictionary.Add(coordSysContext, CoordinateSystemName);

            string axisContext = contextString + ".AxisName";
            dictionary.Add(axisContext, AxisName);

            base.AppendToCSVDictionary(context, dictionary);
        }

        public override void SetElement(URDFElement externalElement)
        {
            base.SetElement(externalElement);

            // The base method already performs the type check, so we don't have to for this cast
            Joint joint = (Joint)externalElement;
            stableId = joint.stableId;

            // These strings aren't kept as URDFAttribute objects and so they are tracked separately
            CoordinateSystemName = joint.CoordinateSystemName;
            AxisName = joint.AxisName;
            CoordinateReference = CopyReference(joint.CoordinateReference);
            AxisReference = CopyReference(joint.AxisReference);
        }

        public override void SetElementFromData(List<string> context, StringDictionary dictionary)
        {
            string contextString = string.Join(".", context);

            string coordSysContext = contextString + ".CoordSysName";
            CoordinateSystemName = dictionary[coordSysContext];

            string axisContext = contextString + ".AxisName";
            AxisName = dictionary[axisContext];

            base.SetElementFromData(context, dictionary);
        }

        public void SetJointKinematics(Joint joint)
        {
            CoordinateSystemName = joint.CoordinateSystemName;
            AxisName = joint.AxisName;
            CoordinateReference = CopyReference(joint.CoordinateReference);
            AxisReference = CopyReference(joint.AxisReference);
            Type = joint.Type;
            Axis.SetElement(joint.Axis);
            Origin.SetElement(joint.Origin);
        }

        public void SetJointNonKinematics(Joint joint)
        {
            Limit.SetElement(joint.Limit);
            Calibration.SetElement(joint.Calibration);
            Dynamics.SetElement(joint.Dynamics);
            Safety.SetElement(joint.Safety);
        }
    }
}
