using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SWSimTool.URDF
{
    //The material element of the visual element.
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Material : URDFElement
    {
        [DataMember]
        public readonly Color Color;

        [DataMember]
        public readonly Texture Texture;

        [DataMember]
        public readonly URDFAttribute NameAttribute;

        public string Name
        {
            get => (string)NameAttribute.Value;
            set => NameAttribute.Value = value;
        }

        public Material() : base("material", false)
        {
            Color = new Color();
            Texture = new Texture();
            NameAttribute = new URDFAttribute("name", true, "");

            Attributes.Add(NameAttribute);
            ChildElements.Add(Color);
            ChildElements.Add(Texture);
        }

        public void FillBoxes(ComboBox box)
        {
            box.Text = Name;
        }
    }
}