using System.Runtime.Serialization;

namespace SWSimTool.URDF
{
    //The inertial element of a link
    [DataContract(IsReference = true, Namespace = SWSimTool.Persistence.DocumentStorageSchema.UrdfNamespace)]
    public class Inertial : URDFElement
    {
        [DataMember]
        public readonly Origin Origin;

        [DataMember]
        public readonly Mass Mass;

        [DataMember]
        public readonly Inertia Inertia;

        public Inertial() : base("inertial", false)
        {
            Origin = new Origin(false);
            Mass = new Mass();
            Inertia = new Inertia();

            ChildElements.Add(Origin);
            ChildElements.Add(Mass);
            ChildElements.Add(Inertia);
        }
    }
}