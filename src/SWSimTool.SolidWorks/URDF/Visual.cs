using System.Runtime.Serialization;

namespace SWSimTool.URDF
{
    //The visual element of a link
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Visual : URDFElement
    {
        [DataMember]
        public readonly Origin Origin;

        [DataMember]
        public readonly Geometry Geometry;

        [DataMember]
        public readonly Material Material;

        public Visual() : base("visual", false)
        {
            Origin = new Origin(false);
            Geometry = new Geometry();
            Material = new Material();

            ChildElements.Add(Origin);
            ChildElements.Add(Geometry);
            ChildElements.Add(Material);
        }
    }
}