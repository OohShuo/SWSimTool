using System.Runtime.Serialization;

namespace SWSimTool.URDF
{
    //The texture element of the material element.
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Texture : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute FilenameAttribute;

        public string Filename
        {
            get => (string)FilenameAttribute.Value;
            set => FilenameAttribute.Value = value;
        }

        [DataMember]
        public string wFilename;

        public Texture() : base("texture", false)
        {
            wFilename = "";
            FilenameAttribute = new URDFAttribute("filename", true, null);

            Attributes.Add(FilenameAttribute);
        }
    }
}