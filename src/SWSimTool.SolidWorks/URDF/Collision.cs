using System.Runtime.Serialization;

namespace SWSimTool.URDF
{
    //The collision element of a link.
    [DataContract(Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Collision : URDFElement
    {
        [DataMember]
        public readonly Origin Origin;

        [DataMember]
        public readonly Geometry Geometry;

        public Collision() : base("collision", false)
        {
            Origin = new Origin(false);
            Geometry = new Geometry();

            ChildElements.Add(Origin);
            ChildElements.Add(Geometry);
        }
    }
}