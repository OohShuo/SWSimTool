using System;
using System.Drawing;
using System.Windows.Forms;
using SWSimTool.URDF;

namespace SWSimTool.UI {
    // A page-owned adapter for the existing Joint.Limit persistence fields.
    public sealed class UrdfJointLimitControl : UserControl {
        readonly TextBox[] inputs={new TextBox(),new TextBox(),new TextBox(),new TextBox()};
        readonly Label[] labels={new Label(),new Label(),new Label(),new Label()};
        readonly bool[] shown=new bool[4];
        bool arranging;
        public int ContentHeight { get; private set; }
        // Keep the native host at least as tall as the measured embedded content.
        // Converting this height with the font's dialog-unit ratio clips the last rows.
        public int PageHeight => HasParameters?ContentHeight+Math.Max(3,Font.Height/4):1;
        public bool HasParameters => shown[2];
        public event EventHandler ContentHeightChanged;
        public UrdfJointLimitControl(){
            AutoScroll=false;Width=260;
            for(int i=0;i<4;i++){labels[i].AutoSize=false;Controls.Add(labels[i]);Controls.Add(inputs[i]);}
            SetType("fixed");
        }
        public void LoadJoint(Joint joint,bool root){
            joint.Limit.FillBoxes(inputs[0],inputs[1],inputs[2],inputs[3],"G17");SetType(root?"fixed":joint.Type);
        }
        public void SetType(string value){
            bool bounded=value=="revolute"||value=="prismatic",active=bounded||value=="continuous";
            string position=value=="prismatic"?"m":"rad",force=value=="prismatic"?"N":"N·m",speed=value=="prismatic"?"m/s":"rad/s";
            string[] text={"下限 ("+position+")","上限 ("+position+")","最大力/力矩 ("+force+")","最大速度 ("+speed+")"};
            for(int i=0;i<4;i++){labels[i].Text=text[i];shown[i]=active&&(i>=2||bounded);labels[i].Visible=inputs[i].Visible=shown[i];}
            ArrangeRows();
        }
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);ArrangeRows();}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);ArrangeRows();}
        void ArrangeRows(){
            if(arranging||inputs==null||labels==null||shown==null)return;
            arranging=true;
            try{
                int gap=Math.Max(3,Font.Height/4),left=gap,labelWidth=Math.Max(40,(ClientSize.Width-3*gap)*52/100),x=left+labelWidth+gap;
                int inputWidth=Math.Max(30,ClientSize.Width-x-gap),y=0;
                for(int i=0;i<4;i++){
                    if(!shown[i])continue;
                    int labelHeight=TextRenderer.MeasureText(labels[i].Text,Font,new Size(labelWidth,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPadding).Height;
                    int rowHeight=Math.Max(inputs[i].PreferredHeight,labelHeight);
                    labels[i].SetBounds(left,y+(rowHeight-labelHeight)/2,labelWidth,labelHeight);
                    inputs[i].SetBounds(x,y+(rowHeight-inputs[i].PreferredHeight)/2,inputWidth,inputs[i].PreferredHeight);
                    y+=rowHeight+gap;
                }
                int height=y==0?0:y-gap;
                bool changed=ContentHeight!=height;
                ContentHeight=height;Height=Math.Max(1,height);
                if(changed)ContentHeightChanged?.Invoke(this,EventArgs.Empty);
            }finally{arranging=false;}
        }
        public void Commit(Joint joint){
            joint.Limit.SetInputs(inputs[0].Text,inputs[1].Text,inputs[2].Text,inputs[3].Text);
        }
    }
}
