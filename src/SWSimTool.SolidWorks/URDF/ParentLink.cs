using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SWSimTool.URDF
{
    //parent_link element of a joint.
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class ParentLink : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute NameAttribute;

        public string Name
        {
            get => (string)NameAttribute.Value;
            set => NameAttribute.Value = value;
        }

        public ParentLink() : base("parent", true)
        {
            NameAttribute = new URDFAttribute("link", true, "");
            Attributes.Add(NameAttribute);
        }

        public void FillBoxes(Label box)
        {
            box.Text = Name;
        }

        public void Update(Label box)
        {
            Name = box.Text;
        }
    }
}