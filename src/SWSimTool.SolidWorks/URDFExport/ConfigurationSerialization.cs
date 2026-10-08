using SolidWorks.Interop.sldworks;
using SWSimTool.URDF;
using SWSimTool.Simulation;
using SWSimTool.Utilities;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
namespace SWSimTool.URDFExport {
 public static class ConfigurationSerialization {
  private static readonly log4net.ILog logger=Logger.GetLogger();
  private const double SerializationVersion=1.4;
  public const string UrdfConfigurationSwAttributeName=SWSimTool.Persistence.DocumentStorageSchema.AttributeName;
  public static LinkNode ReadTree(string data,double version){ValidateVersion(data,version);var tree=DeserializeFromString(data);StableReferences.NormalizeTree(tree);return tree;}
  public static string WriteTree(LinkNode tree)=>tree==null?null:SerializeToString(tree);
  public static string WriteBusinessTree(Link tree)=>tree==null?null:WriteTree(new LinkNode(tree.Clone()));
  static void ValidateVersion(string data,double version){if(!string.IsNullOrWhiteSpace(data)&&(version!=SerializationVersion))throw new InvalidDataException("Unsupported URDF configuration format: "+version);}
  public static void ValidateTreeData(string data,double version){ValidateVersion(data,version);if(!string.IsNullOrWhiteSpace(data)&&DeserializeFromString(data)==null)throw new InvalidDataException("Invalid URDF configuration");}
  public static LinkNode LoadBaseNodeFromModel(ModelDoc2 model,out bool error){error=false;var saved=SimulationStorage.LoadEntry(model);if(saved==null)return null;var tree=ReadTree(saved.urdf_xml,saved.urdf_version);CadTreeReferences.Normalize(model,tree);return tree;}
  public static void SaveConfigTreeXML(SldWorks app,ModelDoc2 model,LinkNode tree,bool warnUser,SimulationProject completeDraft=null){tree=tree==null?null:new LinkNode(tree.Snapshot());completeDraft=completeDraft==null?null:ConfigurationEditingContext.CopyProject(completeDraft);var saved=SimulationStorage.LoadEntry(model);CadTreeReferences.Normalize(model,tree);var data=WriteTree(tree);if(saved!=null&&saved.urdf_xml==data&&(completeDraft==null||ExportFingerprint.Hash(saved.simulation)==ExportFingerprint.Hash(completeDraft)))return;if(warnUser&&MessageBox.Show("Save configuration changes?","SWSimTool",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;SimulationStorage.SaveTree(app,model,data,SerializationVersion,completeDraft);}
        private static void SavePropertiesLinkNodeToLink(LinkNode node)
        {
            if (node.Link == null)
            {
                node.Link = new Link();
                return;
            }

            node.Link.Name = node.Name;
            var linkIdentity = node.Link.StableId;
            if (node.Link.Joint != null) { var jointIdentity = node.Link.Joint.StableId; }

            foreach (LinkNode child in node.Nodes)
            {
                SavePropertiesLinkNodeToLink(child);
            }
        }

        /// <summary>
        /// Data Contract serialization. All members of an object need to be annotated with a
        /// [DataMember] attribute.
        /// </summary>
        /// <param name="node">TreeView LinkNode to serialize</param>
        /// <returns>A string serialized utilizing DataContract serialization XML scheme</returns>
        private static string SerializeToString(LinkNode node)
        {
            node=new LinkNode(node.Snapshot());
            SavePropertiesLinkNodeToLink(node);
            SWSimTool.Simulation.StableReferences.NormalizeTree(node);
            Link link = node.UpdateLinkTree(null);
            string data = "";
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractSerializer ser =
                    new DataContractSerializer(typeof(Link));

                try
                {
                    ser.WriteObject(stream, link);
                    stream.Flush();
                    data = Encoding.ASCII.GetString(stream.GetBuffer(), 0, (int)stream.Position);
                }
                catch (SerializationException e)
                {
                    logger.Error("Serialization failed", e); throw new InvalidDataException("URDF configuration serialization failed",e);
                }
            }
            return data;
        }

        /// <summary>
        /// Read a URDF Link from a serialized string
        /// </summary>
        /// <param name="data">Data string to read into a TreeView LinkNode</param>
        /// <returns>Deserialized LinkNode</returns>
        private static LinkNode DeserializeFromString(string data)
        {
            LinkNode baseNode = null;
            if (!string.IsNullOrWhiteSpace(data))
            {
                using (MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(data)))
                {
                    DataContractSerializer ser =
                        new DataContractSerializer(typeof(Link));

                    try
                    {
                        Link link = (Link)ser.ReadObject(stream);

                        // By copying this link, we can ensure that all non-serialized properties are setup correctly
                        Link copy = link.Clone();
                        baseNode = new LinkNode(copy);
                    }
                    catch (SerializationException e)
                    {
                        logger.Error("Deserialization failed with exception, propagating failure", e);
                        throw new InvalidDataException("URDF configuration XML is invalid", e);
                    }
                }
            }
            return baseNode;
        }


 }
}
