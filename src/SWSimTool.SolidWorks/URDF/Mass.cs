using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SWSimTool.URDF
{
    //mass element, belongs to the inertial element
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Mass : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute ValueAttribute;

        public double Value
        {
            get => (double)ValueAttribute.Value;
            set => ValueAttribute.Value = value;
        }

        public Mass() : base("mass", false)
        {
            ValueAttribute = new URDFAttribute("value", true, 0.0);

            Attributes.Add(ValueAttribute);
        }

        public void FillBoxes(TextBox box, string format)
        {
            box.Text = ValueAttribute.GetTextFromDoubleValue(format);
        }

        public void Update(TextBox box)
        {
            ValueAttribute.SetDoubleValueFromString(box.Text);
        }
    }
}