using System;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SWSimTool.URDF
{
    //The limit element of a joint.
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Limit : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute LowerAttribute;

        [DataMember]
        private readonly URDFAttribute UpperAttribute;

        [DataMember]
        private readonly URDFAttribute EffortAttribute;

        [DataMember]
        private readonly URDFAttribute VelocityAttribute;

        public double Lower
        {
            get => (double)LowerAttribute.Value;
            set => LowerAttribute.Value = value;
        }

        public double Upper
        {
            get => (double)UpperAttribute.Value;
            set => UpperAttribute.Value = value;
        }

        public double Effort
        {
            get => (double)EffortAttribute.Value;
            set => EffortAttribute.Value = value;
        }

        public double Velocity
        {
            get => (double)VelocityAttribute.Value;
            set => VelocityAttribute.Value = value;
        }

        public Limit() : base("limit", false)
        {
            EffortAttribute = new URDFAttribute("effort", false, null);
            VelocityAttribute = new URDFAttribute("velocity", false, null);
            LowerAttribute = new URDFAttribute("lower", false, null);
            UpperAttribute = new URDFAttribute("upper", false, null);

            Attributes.Add(LowerAttribute);
            Attributes.Add(UpperAttribute);
            Attributes.Add(EffortAttribute);
            Attributes.Add(VelocityAttribute);
        }

        public void FillBoxes(TextBox boxLower, TextBox boxUpper,
            TextBox boxEffort, TextBox boxVelocity, string format)
        {
            boxLower.Text = LowerAttribute.GetTextFromDoubleValue(format);
            boxUpper.Text = UpperAttribute.GetTextFromDoubleValue(format);
            boxEffort.Text = EffortAttribute.GetTextFromDoubleValue(format);
            boxVelocity.Text = VelocityAttribute.GetTextFromDoubleValue(format);
        }

        public void ClearBounds(){LowerAttribute.Value=null;UpperAttribute.Value=null;}
        // Empty optional effort/velocity means no configured limit in the
        // plugin. Preserve empty editor values; emit legacy zero placeholders
        // only when a URDF limit element is actually written.
        public override void WriteURDF(System.Xml.XmlWriter writer){
            if(!ElementContainsData())return;
            var copy=new Limit();copy.SetElement(this);copy.SetRequired(IsRequired());
            if(copy.EffortAttribute.Value==null)copy.EffortAttribute.Value=0.0;
            if(copy.VelocityAttribute.Value==null)copy.VelocityAttribute.Value=0.0;
            copy.WriteForData(writer);
        }
        void WriteForData(System.Xml.XmlWriter writer){base.WriteURDF(writer);}
        public void SetInputs(string lower,string upper,string effort,string velocity){
            Func<string,double?> parse=text=>{if(string.IsNullOrWhiteSpace(text))return null;double value;if(!double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value))throw new System.ArgumentException("关节限位参数必须为有效数字。");return value;};
            var l=parse(lower);var u=parse(upper);var e=parse(effort);var v=parse(velocity);
            if(l.HasValue&&u.HasValue&&l>u)throw new System.ArgumentException("关节下限不能大于上限。");
            if(e<0||v<0)throw new System.ArgumentException("最大力/力矩和速度不能为负数。");
            LowerAttribute.Value=l;UpperAttribute.Value=u;EffortAttribute.Value=e;VelocityAttribute.Value=v;
        }
        public void SetValues(TextBox boxLower, TextBox boxUpper,
            TextBox boxEffort, TextBox boxVelocity)
        {
            SetInputs(boxLower.Text,boxUpper.Text,boxEffort.Text,boxVelocity.Text);
        }

        public override void SetRequired(bool required)
        {
            base.SetRequired(required);
            UpperAttribute.SetRequired(required);
            LowerAttribute.SetRequired(required);
        }

        public override bool AreRequiredFieldsSatisfied()
        {
            return base.AreRequiredFieldsSatisfied();
        }
    }
}
