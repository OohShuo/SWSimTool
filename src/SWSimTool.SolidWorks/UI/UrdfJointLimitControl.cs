using System;
using System.Windows.Forms;
using SWSimTool.URDF;

namespace SWSimTool.UI {
    // A page-owned adapter for the existing Joint.Limit persistence fields.
    public sealed class UrdfJointLimitControl : UserControl {
        readonly TextBox[] inputs={new TextBox(),new TextBox(),new TextBox(),new TextBox()};
        readonly Label[] labels={new Label(),new Label(),new Label(),new Label()};
        readonly TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2};
        string type;
        public UrdfJointLimitControl(){
            AutoScroll=true;layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));Controls.Add(layout);
            for(int i=0;i<4;i++){labels[i].AutoSize=true;inputs[i].Dock=DockStyle.Fill;layout.Controls.Add(labels[i],0,i);layout.Controls.Add(inputs[i],1,i);}
        }
        public void LoadJoint(Joint joint,bool root){
            joint.Limit.FillBoxes(inputs[0],inputs[1],inputs[2],inputs[3],"G17");SetType(root?"fixed":joint.Type);
        }
        public void SetType(string value){
            type=value;bool bounded=value=="revolute"||value=="prismatic",active=bounded||value=="continuous";
            string position=value=="prismatic"?"m":"rad",force=value=="prismatic"?"N":"N·m",speed=value=="prismatic"?"m/s":"rad/s";
            string[] text={"下限 ("+position+")","上限 ("+position+")","最大力/力矩 ("+force+")","最大速度 ("+speed+")"};
            for(int i=0;i<4;i++){labels[i].Text=text[i];labels[i].Visible=inputs[i].Visible=active&&(i>=2||bounded);}
        }
        public void Commit(Joint joint){
            joint.Limit.SetInputs(inputs[0].Text,inputs[1].Text,inputs[2].Text,inputs[3].Text);
        }
    }
}
