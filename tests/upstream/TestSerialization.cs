using Microsoft.VisualStudio.TestTools.UnitTesting;
using SolidWorks.Interop.sldworks;
using SWSimTool.URDF;
using SWSimTool.URDFExport;
using Xunit;

namespace SWSimTool.Test
{
    [Collection("Requires SW Test Collection")]
    public class TestSerialization : SWSimToolTest
    {
        public TestSerialization(SWTestFixture fixture) : base(fixture)
        {
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        public void TestLoadBaseNodeFromModel(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Xunit.Assert.False(error);
            Xunit.Assert.NotNull(baseNode);
            Xunit.Assert.Equal(expNumLinks, CommonSwOperations.GetCount(baseNode.RebuildLink()));
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("4_WHEELER")]
        public void TestSerializeToString(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Xunit.Assert.False(error);

            PrivateType serialization = new PrivateType(typeof(ConfigurationSerialization));
            string newData = (string)serialization.InvokeStatic(
                "SerializeToString", new object[] { baseNode });
            Xunit.Assert.NotNull(newData);
            Xunit.Assert.NotEmpty(newData);

        }
    }
}