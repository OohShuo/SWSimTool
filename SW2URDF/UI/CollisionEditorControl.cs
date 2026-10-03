using SW2URDF.Simulation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    public sealed class CollisionEditorControl : UserControl
    {
        readonly AttachmentService service;
        readonly SimulationProject draft;
        readonly CollisionPreview preview;
        readonly ComboBox link=new ComboBox(), shape=new ComboBox(), definition=new ComboBox(), mode=new ComboBox(), extrusion=new ComboBox();
        readonly ListBox list=new ListBox(), pairs=new ListBox();
        readonly TextBox name=new TextBox(), dimensions=new TextBox(), xyz=new TextBox(), rpy=new TextBox(), offset=new TextBox(), rotation=new TextBox(), total=new TextBox(), thickness=new TextBox();
        readonly Label status=new Label(), referenceInfo=new Label();
        readonly CheckBox show=new CheckBox{Text="显示全部碰撞几何体",Checked=true}, disable=new CheckBox{Text="默认排除内部 link 碰撞",Checked=true};
        readonly ComboBox pairA=new ComboBox(), pairB=new ComboBox();
        readonly Timer timer=new Timer{Interval=180};
        CollisionGeometry current;
        bool loading;
        int armed=-1;
        public Action BeginSelection { get; set; }
        public CollisionEditorControl(AttachmentService service, string selectedLink)
        {
            this.service=service;
            draft=Clone(service.Project); draft.collision=draft.collision??new CollisionConfiguration();
            preview=new CollisionPreview(service); Dock=DockStyle.Fill; AutoScroll=true;
            var rows=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(3)}; rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Controls.Add(rows);
            Action<Control> add=c=>{c.Dock=DockStyle.Top; c.Margin=new Padding(0,3,0,3); rows.Controls.Add(c);};
            Action<string,Control> field=(label,c)=>{add(new Label{Text=label,AutoSize=true});add(c);};
            foreach(var combo in new[]{link,shape,definition,mode,extrusion,pairA,pairB}) combo.DropDownStyle=ComboBoxStyle.DropDownList;
            var links=service.LinkTransforms().Keys.ToArray(); link.Items.AddRange(links); pairA.Items.AddRange(links);pairB.Items.AddRange(links);
            field("所属 link",link); mode.Items.AddRange(new[]{"原网格","简单几何体","无碰撞"}); field("该 link 的碰撞模式",mode);
            list.Height=85;add(list); var buttons=new FlowLayoutPanel{AutoSize=true};
            var create=new Button{Text="添加",AutoSize=true};var remove=new Button{Text="删除",AutoSize=true};buttons.Controls.Add(create);buttons.Controls.Add(remove);add(buttons);
            field("名称",name);shape.Items.AddRange(new[]{"box","sphere","cylinder","capsule"});field("形状",shape);field("定义方式",definition);
            add(new Label{Text="先点击拾取，再在模型中选择对象。\n参考按当前定义方式的顺序填写。",AutoSize=true});
            var pick=new FlowLayoutPanel{AutoSize=true}; for(int i=0;i<3;i++){int slot=i;var b=new Button{Text="拾取"+(i+1),Width=65};b.Click+=(s,e)=>{armed=slot;status.Text="请在模型中选择参考 "+(slot+1);BeginSelection?.Invoke();};pick.Controls.Add(b);}add(pick);
            referenceInfo.AutoSize=true;add(referenceInfo);
            field("尺寸 mm：box=长 宽 高；球=半径；轴体=半径 圆柱段长度",dimensions);
            field("胶囊总长度 mm（总长度方式）",total);field("矩形面厚度 mm",thickness);extrusion.Items.AddRange(new[]{"两侧对称","沿面法向","反面法向"});field("矩形面延伸",extrusion);
            field("局部位置 mm：x y z（手动方式）",xyz);field("局部方向 °：roll pitch yaw（手动方式）",rpy);
            field("参考几何体局部偏移 mm：x y z",offset);field("参考几何体局部旋转 °：roll pitch yaw",rotation);
            add(show); add(disable);field("允许碰撞：link 1",pairA);field("link 2",pairB);
            var pairButtons=new FlowLayoutPanel{AutoSize=true}; var allow=new Button{Text="允许此对",AutoSize=true};var deny=new Button{Text="删除此对",AutoSize=true};pairButtons.Controls.Add(allow);pairButtons.Controls.Add(deny);add(pairButtons);pairs.Height=65;add(pairs);
            var save=new Button{Text="保存到装配配置节点",AutoSize=true};add(save);status.AutoSize=true;status.ForeColor=Color.DarkRed;add(status);
            timer.Tick+=(s,e)=>{timer.Stop();UpdatePreview();};
            foreach(var box in new[]{name,dimensions,xyz,rpy,offset,rotation,total,thickness}) box.TextChanged+=(s,e)=>Schedule();
            extrusion.SelectedIndexChanged+=(s,e)=>Schedule();show.CheckedChanged+=(s,e)=>Schedule();
            link.SelectedIndexChanged+=(s,e)=>{if(loading)return;Guard(ReadCurrent);RefreshList();};
            mode.SelectedIndexChanged+=(s,e)=>{if(loading)return;draft.collision.link_modes[(string)link.SelectedItem]=new[]{"mesh","primitive","none"}[mode.SelectedIndex];Schedule();};
            list.SelectedIndexChanged+=(s,e)=>{if(loading)return;Guard(ReadCurrent);current=list.SelectedItem as CollisionGeometry;LoadCurrent();};
            shape.SelectedIndexChanged+=(s,e)=>{if(loading||current==null)return;current.type=(string)shape.SelectedItem;current.size=current.type=="box"?new[]{.05,.04,.03}:current.type=="sphere"?new[]{.01}:new[]{.01,.05};current.definition="manual";current.references.Clear();LoadCurrent();};
            definition.SelectedIndexChanged+=(s,e)=>{if(loading||current==null)return;current.definition=((Choice)definition.SelectedItem).Key;current.references.Clear();LoadCurrent();};
            create.Click+=(s,e)=>{if(link.SelectedItem==null)return;var g=new CollisionGeometry{link=(string)link.SelectedItem,name="collision_"+Guid.NewGuid().ToString("N").Substring(0,8)};draft.collision.geometries.Add(g);draft.collision.link_modes[g.link]="primitive";RefreshList(g);};
            remove.Click+=(s,e)=>{if(current==null)return;draft.collision.geometries.Remove(current);if(!draft.collision.geometries.Any(g=>g.link==current.link)&&draft.collision.link_modes[current.link]=="primitive")draft.collision.link_modes[current.link]="none";RefreshList();};
            allow.Click+=(s,e)=>Guard(()=>{string a=(string)pairA.SelectedItem,b=(string)pairB.SelectedItem;if(a==null||b==null||a==b)throw new InvalidOperationException("请选择两个不同的 link。");if(!draft.collision.allowed_pairs.Any(p=>(p.link1==a&&p.link2==b)||(p.link1==b&&p.link2==a)))draft.collision.allowed_pairs.Add(new CollisionPair{link1=a,link2=b});RefreshPairs();});
            deny.Click+=(s,e)=>{if(pairs.SelectedItem is CollisionPair p){draft.collision.allowed_pairs.Remove(p);RefreshPairs();}};
            save.Click+=(s,e)=>Guard(Save);
            pairA.SelectedIndex=links.Length>0?0:-1;pairB.SelectedIndex=links.Length>1?1:-1;
            disable.Checked=draft.collision.disable_internal;link.SelectedItem=links.Contains(selectedLink)?selectedLink:links.FirstOrDefault();RefreshPairs();
        }
        static T Clone<T>(T value) { var s=new JavaScriptSerializer();return s.Deserialize<T>(s.Serialize(value)); }
        sealed class Choice {public string Key,Label;public override string ToString()=>Label;}
        static string Format(double[] values,double scale=1)=>string.Join(" ",values.Select(v=>(v*scale).ToString("G9",CultureInfo.InvariantCulture)));
        static double[] Parse(string value,int count,double scale=1)
        {
            var parts=value.Split(new[]{' ',',',';'},StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length!=count)throw new InvalidOperationException("需要 "+count+" 个数字，以空格分隔。");
            return parts.Select(v=>double.Parse(v,CultureInfo.InvariantCulture)*scale).ToArray();
        }
        void RefreshList(CollisionGeometry select=null)
        {
            loading=true;list.Items.Clear();foreach(var g in draft.collision.geometries.Where(g=>g.link==(string)link.SelectedItem))list.Items.Add(g);
            string m;draft.collision.link_modes.TryGetValue((string)link.SelectedItem,out m);mode.SelectedIndex=m=="primitive"?1:m=="none"?2:0;
            list.SelectedItem=select??list.Items.Cast<CollisionGeometry>().FirstOrDefault();current=list.SelectedItem as CollisionGeometry;loading=false;LoadCurrent();
        }
        void LoadCurrent()
        {
            loading=true;definition.Items.Clear();var choices=new List<Choice>{new Choice{Key="manual",Label="手动位姿与尺寸"},new Choice{Key="frame",Label="坐标系 + 尺寸"},new Choice{Key="center",Label="中心点 + link 方向 + 尺寸"},new Choice{Key="center_frame",Label="中心点 + 方向坐标系 + 尺寸"}};
            if(current!=null){
                if(current.type=="box")choices.Add(new Choice{Key="rectangle_face",Label="矩形面 + 厚度"});
                if(current.type=="sphere"){choices.Add(new Choice{Key="radius_points",Label="球心 + 球面一点"});choices.Add(new Choice{Key="sphere_face",Label="选择球面"});}
                if(current.type=="cylinder"||current.type=="capsule")choices.Add(new Choice{Key="endpoints",Label=current.type=="capsule"?"两端球心 + 半径":"两端面中心点 + 半径"});
                if(current.type=="cylinder")choices.Add(new Choice{Key="cylinder_face",Label="圆柱面 + 两个轴向范围点"});
                if(current.type=="capsule")choices.Add(new Choice{Key="frame_total",Label="坐标系 + 半径 + 总长度"});
                foreach(var c in choices)definition.Items.Add(c);shape.SelectedItem=current.type;definition.SelectedItem=choices.First(c=>c.Key==current.definition);
                name.Text=current.name;dimensions.Text=Format(current.size,1000);xyz.Text=Format(current.xyz,1000);rpy.Text=Format(current.rpy,180/Math.PI);offset.Text=Format(current.offset_xyz,1000);rotation.Text=Format(current.offset_rpy,180/Math.PI);total.Text=(current.length_input*1000).ToString(CultureInfo.InvariantCulture);thickness.Text=(current.thickness*1000).ToString(CultureInfo.InvariantCulture);extrusion.SelectedIndex=current.extrusion==1?1:current.extrusion==-1?2:0;
                referenceInfo.Text=string.Join("\n",current.references.Select((r,i)=>(i+1)+": "+r.label));
            } else {name.Text="";referenceInfo.Text="先添加或选择一个几何体。";}
            foreach(var c in new Control[]{name,shape,definition,dimensions,total,thickness,extrusion,offset,rotation})c.Enabled=current!=null;
            xyz.Enabled=rpy.Enabled=current!=null&&current.definition=="manual";loading=false;Schedule();
        }
        public void CaptureSelected()
        {
            if(armed<0||current==null)return;
            Guard(()=>{var r=service.CaptureSelection();if(armed>current.references.Count)throw new InvalidOperationException("请按参考 1、2、3 的顺序拾取。");if(armed==current.references.Count)current.references.Add(r);else current.references[armed]=r;armed=-1;referenceInfo.Text=string.Join("\n",current.references.Select((v,i)=>(i+1)+": "+v.label));Schedule();});
        }
        void Schedule(){if(loading)return;timer.Stop();timer.Start();}
        void ReadCurrent()
        {
            if(current==null)return;
            current.name=name.Text.Trim();current.size=Parse(dimensions.Text,current.type=="box"?3:current.type=="sphere"?1:2,.001);
            current.offset_xyz=Parse(offset.Text,3,.001);current.offset_rpy=Parse(rotation.Text,3,Math.PI/180);
            current.length_input=Parse(total.Text,1,.001)[0];current.thickness=Parse(thickness.Text,1,.001)[0];current.extrusion=extrusion.SelectedIndex==1?1:extrusion.SelectedIndex==2?-1:0;
            if(current.definition=="manual"){current.xyz=Parse(xyz.Text,3,.001);current.rpy=current.type=="sphere"?new double[3]:Parse(rpy.Text,3,Math.PI/180);}
            service.ResolveCollision(current);
            if(current.definition!="manual"){loading=true;xyz.Text=Format(current.xyz,1000);rpy.Text=Format(current.rpy,180/Math.PI);dimensions.Text=Format(current.size,1000);loading=false;}
        }
        void UpdatePreview()
        {
            try{ReadCurrent();foreach(var g in draft.collision.geometries)if(g!=current)service.ResolveCollision(g);preview.Clear();if(show.Checked)preview.Show(draft.collision.geometries,current?.id);status.Text="实时预览已更新；保存配置后仍需保存装配文件。";}
            catch(Exception e){preview.Clear();status.Text=e.Message;}
        }
        void RefreshPairs(){pairs.Items.Clear();foreach(var p in draft.collision.allowed_pairs)pairs.Items.Add(p);}
        void Guard(Action action){try{action();}catch(Exception e){status.Text=e.Message;}}
        public void Save()
        {
            timer.Stop();ReadCurrent();
            var frames=service.LinkTransforms();var names=new HashSet<string>();
            foreach(var g in draft.collision.geometries){service.ResolveCollision(g);if(!names.Add(g.name))throw new InvalidOperationException("碰撞几何体名称重复："+g.name);}
            foreach(var m in draft.collision.link_modes){if(!frames.ContainsKey(m.Key))throw new InvalidOperationException("link 已失效："+m.Key);if(m.Value=="primitive"&&!draft.collision.geometries.Any(g=>g.link==m.Key))throw new InvalidOperationException("简单几何体模式至少需要一个几何体。");}
            foreach(var p in draft.collision.allowed_pairs){string a,b;if(!frames.ContainsKey(p.link1)||!frames.ContainsKey(p.link2))throw new InvalidOperationException("允许碰撞对的 link 已失效。");draft.collision.link_modes.TryGetValue(p.link1,out a);draft.collision.link_modes.TryGetValue(p.link2,out b);if(a=="none"||b=="none")throw new InvalidOperationException("允许碰撞对不能引用无碰撞 link。");}
            draft.collision.disable_internal=disable.Checked;var previous=service.Project;service.Project=Clone(draft);
            try{service.Save();}catch{service.Project=previous;throw;}status.Text="已写入装配配置节点，请保存 .sldasm 文件。";
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();preview.Dispose();}base.Dispose(disposing);}
    }
}
