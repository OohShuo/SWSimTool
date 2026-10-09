using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SWSimTool.URDF;

namespace SWSimTool.UI {
    // Native fields participate in the PropertyManager layout; no embedded HWND.
    public sealed class UrdfJointLimitFields {
        readonly IPropertyManagerPageTextbox[] inputs=new IPropertyManagerPageTextbox[4];
        readonly IPropertyManagerPageLabel[] labels=new IPropertyManagerPageLabel[4];
        public UrdfJointLimitFields(IPropertyManagerPageGroup group){
            int options=(int)(swAddControlOptions_e.swControlOptions_Visible|swAddControlOptions_e.swControlOptions_Enabled|swAddControlOptions_e.swControlOptions_SmallGapAbove);
            for(int i=0;i<4;i++){
                labels[i]=(IPropertyManagerPageLabel)group.AddControl2(120+i*2,(short)swPropertyManagerPageControlType_e.swControlType_Label,"",(short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent,options,"使用 SI 单位；留空表示未设置");
                inputs[i]=(IPropertyManagerPageTextbox)group.AddControl2(121+i*2,(short)swPropertyManagerPageControlType_e.swControlType_Textbox,"",(short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent,options,"使用 SI 单位；留空表示未设置");
            }
            SetType("fixed");
        }
        public void LoadJoint(Joint joint,bool root){
            string[] values=joint.Limit.GetInputTexts("G17");
            for(int i=0;i<4;i++)inputs[i].Text=values[i];
            SetType(root?"fixed":joint.Type);
        }
        public void SetType(string value){
            bool bounded=value=="revolute"||value=="prismatic",active=bounded||value=="continuous";
            string position=value=="prismatic"?"m":"rad",force=value=="prismatic"?"N":"N·m",speed=value=="prismatic"?"m/s":"rad/s";
            string[] captions={"下限 ("+position+")","上限 ("+position+")","最大力/力矩 ("+force+")","最大速度 ("+speed+")"};
            for(int i=0;i<4;i++){
                labels[i].Caption=captions[i];
                ((IPropertyManagerPageControl)labels[i]).Visible=active&&(i>=2||bounded);
                ((IPropertyManagerPageControl)inputs[i]).Visible=active&&(i>=2||bounded);
            }
        }
        public void Commit(Joint joint){joint.Limit.SetInputs(inputs[0].Text,inputs[1].Text,inputs[2].Text,inputs[3].Text);}
    }
}