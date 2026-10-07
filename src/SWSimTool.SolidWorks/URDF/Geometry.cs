using System.Runtime.Serialization;

namespace SWSimTool.URDF
{
    //The geometry element of the visual and collision elements
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Geometry : URDFElement
    {
        [DataMember]
        public readonly Mesh Mesh;

        public Geometry() : base("geometry", true)
        {
            Mesh = new Mesh();
            ChildElements.Add(Mesh);
        }
    }
}