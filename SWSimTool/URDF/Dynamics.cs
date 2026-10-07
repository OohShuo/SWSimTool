using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SWSimTool.URDF
{
    //The dynamics element of a joint.
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Dynamics : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute DampingAttribute;

        public double Damping
        {
            get => (double)DampingAttribute.Value;
            set => DampingAttribute.Value = value;
        }

        [DataMember]
        private readonly URDFAttribute FrictionAttribute;

        public double Friction
        {
            get => (double)FrictionAttribute.Value;
            set => FrictionAttribute.Value = value;
        }

        public Dynamics() : base("dynamics", false)
        {
            DampingAttribute = new URDFAttribute("damping", false, null);
            FrictionAttribute = new URDFAttribute("friction", false, null);

            Attributes.Add(DampingAttribute);
            Attributes.Add(FrictionAttribute);
        }

        public void FillBoxes(TextBox boxDamping, TextBox boxFriction, string format)
        {
            boxDamping.Text = DampingAttribute.GetTextFromDoubleValue(format);
            boxFriction.Text = FrictionAttribute.GetTextFromDoubleValue(format);
        }

        public void SetValues(TextBox boxDamping, TextBox boxFriction)
        {
            DampingAttribute.SetDoubleValueFromString(boxDamping.Text);
            FrictionAttribute.SetDoubleValueFromString(boxFriction.Text);
        }
    }
}