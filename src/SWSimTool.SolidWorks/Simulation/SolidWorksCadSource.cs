using SolidWorks.Interop.sldworks;
namespace SWSimTool.Simulation {
    internal sealed class SolidWorksCadSource:ICadSource {
        readonly SldWorks app;
        readonly ModelDoc2 document;
        internal SolidWorksCadSource(SldWorks app,ModelDoc2 document){this.app=app;this.document=document;}
        public CadSourceSnapshot BuildNative(string workspace,SimulationProject project)=>ProjectSourceBuilder.BuildNative(app,document,workspace,project);
    }
}
