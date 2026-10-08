using SWSimTool.Simulation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SWSimTool.UI
{
    public sealed class CollisionTabPage : TabPage
    {
        public CollisionTabPage(string text):base(text){AutoScroll=true;}
        // Native PropertyManager focus must not snap the scroller back after a wheel event.
        protected override Point ScrollToControl(Control activeControl){return DisplayRectangle.Location;}
    }
    public sealed class CollisionComboBox : ComboBox
    {
        public CollisionComboBox(){DropDownStyle=ComboBoxStyle.DropDownList;DropDownWidth=320;}
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x020A){
                int delta=unchecked((short)((m.WParam.ToInt64()>>16)&0xffff));
                for(Control p=Parent;p!=null;p=p.Parent){var scroll=p as ScrollableControl;if(scroll!=null&&scroll.AutoScroll){
                    scroll.AutoScrollPosition=new Point(0,Math.Max(0,-scroll.AutoScrollPosition.Y-delta/120*60));break;
                }}
                m.Result=IntPtr.Zero;return;
            }
            base.WndProc(ref m);
        }
    }
    public sealed class CollisionEditorControl : UserControl
    {
        readonly AttachmentService service;
        readonly SimulationProject draft;
        readonly Dictionary<string,string> linkIds;
        readonly CollisionPreview preview;
        readonly ComboBox link=new CollisionComboBox(),shape=new CollisionComboBox(),definition=new CollisionComboBox(),mode=new CollisionComboBox(),extrusion=new CollisionComboBox(),axis=new CollisionComboBox(),axisSign=new CollisionComboBox();
        readonly ComboBox[] signs={new CollisionComboBox(),new CollisionComboBox(),new CollisionComboBox()};
        readonly ListBox list=new ListBox(),pairs=new ListBox();
        readonly TextBox name=new TextBox();
        readonly TextBox[] size={new TextBox(),new TextBox(),new TextBox()},xyz={new TextBox(),new TextBox(),new TextBox()},rpy={new TextBox(),new TextBox(),new TextBox()},offset={new TextBox(),new TextBox(),new TextBox()},rotation={new TextBox(),new TextBox(),new TextBox()};
        readonly TextBox total=new TextBox(),thickness=new TextBox();
        readonly Label status=new Label(),referenceInfo=new Label();
        readonly CheckBox show=new CheckBox{Text="显示碰撞几何体",Checked=true},disable=new CheckBox{Text="默认排除内部 link 碰撞",Checked=true};
        readonly ComboBox pairA=new CollisionComboBox(),pairB=new CollisionComboBox();
        readonly HashSet<TextBox> solverInvalid=new HashSet<TextBox>();
        readonly Timer timer=new Timer{Interval=180},cadTimer=new Timer{Interval=500};
        readonly Dictionary<Control,Control> rows=new Dictionary<Control,Control>();
        readonly Dictionary<string,Control> dimensionRows=new Dictionary<string,Control>();
        readonly Dictionary<string,Button> edgeButtons=new Dictionary<string,Button>();
        readonly HashSet<string> activeDimensions=new HashSet<string>();
        readonly Button[] picks=new Button[3];
        CollisionGeometry current;
        bool loading;
        int armed=-1;
        string armedDimension,revision,configuration,displayedLink;
        bool refreshReferences=true;
        public Action BeginSelection { get; set; }
        public CollisionEditorControl(AttachmentService service,string selectedLink)
        {
            this.service=service;draft=Clone(service.Project);draft.collision=draft.collision??new CollisionConfiguration();
            var identities=service.LinkIdentities();StableReferences.MigrateLegacyLinkModesByName(draft,identities);linkIds=identities.ToDictionary(x=>x.Value,x=>x.Key);
            preview=new CollisionPreview(service);Dock=DockStyle.Fill;AutoScroll=false;
            configuration=service.Model.ConfigurationManager.ActiveConfiguration.Name;revision=service.CollisionRevision;
            var tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);
            var geometryTab=new CollisionTabPage("几何体");var pairTab=new CollisionTabPage("碰撞对");tabs.TabPages.Add(geometryTab);tabs.TabPages.Add(pairTab);
            var geometry=Layout(geometryTab);var contacts=Layout(pairTab);
            var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
            var refreshCAD=new Button{Text="刷新 CAD 参考",AutoSize=true};refreshCAD.Click+=(s,e)=>{service.InvalidateCADCache();refreshReferences=true;Schedule();};footer.Controls.Add(refreshCAD);var save=new Button{Text="保存到装配配置节点",AutoSize=true};footer.Controls.Add(save);status.AutoSize=true;status.MaximumSize=new Size(350,0);status.ForeColor=Color.DarkRed;footer.Controls.Add(status);Controls.Add(footer);
            var links=service.LinkTransforms().Keys.ToArray();link.Items.AddRange(links);pairA.Items.AddRange(links);pairB.Items.AddRange(links);
            Field(geometry,"所属 link",link);mode.Items.AddRange(new[]{"原网格","简单几何体","无碰撞"});Field(geometry,"碰撞模式",mode);
            list.Height=65;Add(geometry,list);var buttons=new FlowLayoutPanel{AutoSize=true};var create=new Button{Text="添加"};var remove=new Button{Text="删除"};buttons.Controls.Add(create);buttons.Controls.Add(remove);Add(geometry,buttons);
            var rebind=new Button{Text="重新绑定到所选 link",AutoSize=true};Add(geometry,rebind);
            rebind.Click+=(s,e)=>Guard(()=>{if(current==null||link.SelectedItem==null)return;service.RequireCurrentDocument();string target=(string)link.SelectedItem;if(MessageBox.Show("将几何体 '"+current.name+"' 绑定到 '"+target+"'。CAD 参考将重新计算；手工相对位姿保留当前值。继续？","重新绑定碰撞几何体",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;current.link=target;current.link_id=linkIds[target];RefreshList(current);Schedule();});
            var clean=new Button{Text="清理失效 link 的关联配置",AutoSize=true};Add(geometry,clean);
            clean.Click+=(s,e)=>Guard(()=>{service.RequireCurrentDocument();var missing=new HashSet<string>(draft.collision.link_modes_by_id.Keys.Where(id=>!linkIds.Values.Contains(id)));foreach(var g in draft.collision.geometries)if(!string.IsNullOrEmpty(g.link_id)&&!linkIds.Values.Contains(g.link_id))missing.Add(g.link_id);if(missing.Count==0){status.Text="没有失效 link 的碰撞配置。";return;}var plan=ConfigurationDependencies.Plan(draft,links:missing);if(MessageBox.Show("失效 ID：\n"+string.Join("\n",missing)+"\n\n将清理：\n"+string.Join("\n",plan.Affected),"清理失效关联",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;service.RequireCurrentDocument();plan.ApplyTo(draft);current=null;RefreshList();RefreshPairs();Schedule();});
            Field(geometry,"名称",name);shape.Items.AddRange(new[]{"box","sphere","cylinder","capsule"});Field(geometry,"形状",shape);Field(geometry,"标定方式",definition);
            for(int i=0;i<3;i++){int slot=i;picks[i]=new Button{AutoSize=true};picks[i].Click+=(s,e)=>{armed=slot;armedDimension=null;status.Text="请在模型中选择："+picks[slot].Text;BeginSelection?.Invoke();};Add(geometry,picks[i]);}
            referenceInfo.AutoSize=true;Add(geometry,referenceInfo);
            for(int i=0;i<3;i++)Dimension(geometry,"size"+i,"尺寸 "+(i+1)+" mm",size[i]);
            Dimension(geometry,"total","胶囊总长度 mm",total);Dimension(geometry,"thickness","矩形面厚度 mm",thickness);
            extrusion.Items.AddRange(new[]{"两侧对称","沿面法向","反面法向"});Field(geometry,"面延伸",extrusion);
            axis.Items.AddRange(new[]{"X","Y","Z"});axisSign.Items.AddRange(new[]{"正方向","负方向"});Field(geometry,"拉伸轴",axis);Field(geometry,"拉伸方向",axisSign);
            for(int i=0;i<3;i++){signs[i].Items.AddRange(new[]{"正方向","负方向"});Field(geometry,"XYZ"[i]+" 延伸",signs[i]);}
            for(int i=0;i<3;i++){Field(geometry,"位置 "+"XYZ"[i]+" mm",xyz[i]);Field(geometry,new[]{"Roll °","Pitch °","Yaw °"}[i],rpy[i]);}
            for(int i=0;i<3;i++){Field(geometry,"局部偏移 "+"XYZ"[i]+" mm",offset[i]);Field(geometry,"局部旋转 "+new[]{"Roll °","Pitch °","Yaw °"}[i],rotation[i]);}
            Add(geometry,show);
            Add(contacts,disable);Field(contacts,"link 1",pairA);Field(contacts,"link 2",pairB);
            var pairButtons=new FlowLayoutPanel{AutoSize=true};var allow=new Button{Text="允许此对"};var deny=new Button{Text="删除此对"};pairButtons.Controls.Add(allow);pairButtons.Controls.Add(deny);Add(contacts,pairButtons);pairs.Height=110;Add(contacts,pairs);
            var pairSettings=new Panel{AutoSize=true,Dock=DockStyle.Top};Add(contacts,pairSettings);CollisionPair editingPair=null;bool selectingPair=false;pairs.SelectedIndexChanged+=(s,e)=>{if(selectingPair)return;if(solverInvalid.Count>0&&editingPair!=null&&pairs.Items.Contains(editingPair)){selectingPair=true;pairs.SelectedItem=editingPair;selectingPair=false;status.Text="请先修正当前碰撞对的无效数字。";return;}foreach(Control c in pairSettings.Controls.Cast<Control>().ToArray())c.Dispose();var selected=pairs.SelectedItem as CollisionPair;editingPair=selected;if(selected!=null)pairSettings.Controls.Add(new SolverParametersControl(selected.solver,draft.solver?.contact??ConstraintSettings.Contact(),true,v=>selected.solver=v,solverInvalid));};
            Add(contacts,new Label{Text="添加后保存装配，并重新导出 MJCF。\n无碰撞模式的 link 不能加入允许列表。",AutoSize=true});
            timer.Tick+=(s,e)=>{timer.Stop();UpdatePreview(false);};
            cadTimer.Tick+=(s,e)=>{
                try{string next=service.CollisionRevision;if(next==revision)return;revision=next;
                    if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration){timer.Stop();cadTimer.Stop();preview.Clear();Enabled=false;status.Text="SW Configuration 已切换，请关闭后重新进入碰撞配置。";return;}
                    UpdatePreview(true);
                }catch(Exception ex){status.Text=ex.Message;}
            };
            foreach(var box in size.Concat(xyz).Concat(rpy).Concat(offset).Concat(rotation).Concat(new[]{total,thickness}))box.TextChanged+=(s,e)=>Schedule();
            name.TextChanged+=(s,e)=>{if(loading||current==null)return;current.name=name.Text.Trim();RefreshNames();};
            foreach(var combo in new[]{extrusion,axis,axisSign}.Concat(signs))combo.SelectedIndexChanged+=(s,e)=>Schedule();show.CheckedChanged+=(s,e)=>Schedule();
            link.SelectedIndexChanged+=(s,e)=>{if(loading)return;try{ReadCurrent(true);RefreshList();}catch(Exception ex){loading=true;link.SelectedItem=displayedLink;loading=false;status.Text=ex.Message;}};
            mode.SelectedIndexChanged+=(s,e)=>{if(loading||link.SelectedItem==null)return;draft.collision.SetMode(linkIds[(string)link.SelectedItem],new[]{"mesh","primitive","none"}[mode.SelectedIndex]);};
            list.SelectedIndexChanged+=(s,e)=>{if(loading)return;try{ReadCurrent(true);current=list.SelectedItem as CollisionGeometry;LoadCurrent();}catch(Exception ex){loading=true;list.SelectedItem=current;loading=false;status.Text=ex.Message;}};
            shape.SelectedIndexChanged+=(s,e)=>{if(loading||current==null)return;current.type=(string)shape.SelectedItem;current.size=current.type=="box"?new[]{.05,.04,.03}:current.type=="sphere"?new[]{.01}:new[]{.01,.05};current.definition="manual";current.references.Clear();current.dimension_references.Clear();LoadCurrent();RefreshNames();};
            definition.SelectedIndexChanged+=(s,e)=>{if(loading||current==null)return;Guard(()=>ReadCurrent(true));current.definition=((Choice)definition.SelectedItem).Key;current.references.Clear();current.dimension_references.Clear();LoadCurrent();};
            create.Click+=(s,e)=>Guard(()=>{ReadCurrent(true);if(link.SelectedItem==null)return;var g=new CollisionGeometry{link=(string)link.SelectedItem,link_id=linkIds[(string)link.SelectedItem],name="collision_"+Guid.NewGuid().ToString("N").Substring(0,8)};draft.collision.geometries.Add(g);draft.collision.SetMode(g.link_id,"primitive");RefreshList(g);});
            remove.Click+=(s,e)=>{if(current==null)return;string owner=current.link;draft.collision.geometries.Remove(current);if(linkIds.ContainsKey(owner)&&!draft.collision.geometries.Any(g=>g.link==owner)&&draft.collision.Mode(linkIds[owner])=="primitive")draft.collision.SetMode(linkIds[owner],"none");current=null;RefreshList();};
            allow.Click+=(s,e)=>Guard(()=>{if(solverInvalid.Count>0)throw new InvalidOperationException("请先修正当前碰撞对的无效数字。");string a=(string)pairA.SelectedItem,b=(string)pairB.SelectedItem;if(a==null||b==null||a==b)throw new InvalidOperationException("请选择两个不同的 link。");if(!draft.collision.allowed_pairs.Any(p=>(p.link1==a&&p.link2==b)||(p.link1==b&&p.link2==a)))draft.collision.allowed_pairs.Add(new CollisionPair{link1=a,link2=b});RefreshPairs();});
            deny.Click+=(s,e)=>{var p=pairs.SelectedItem as CollisionPair;if(p!=null){draft.collision.allowed_pairs.Remove(p);RefreshPairs();}};
            save.Click+=(s,e)=>Guard(Save);disable.Checked=draft.collision.disable_internal;
            pairA.SelectedIndex=links.Length>0?0:-1;pairB.SelectedIndex=links.Length>1?1:-1;
            loading=true;link.SelectedItem=links.Contains(selectedLink)?selectedLink:links.FirstOrDefault();loading=false;RefreshList();RefreshPairs();cadTimer.Start();
        }
        static TableLayoutPanel Layout(Control parent){var p=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(4)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));parent.Controls.Add(p);return p;}
        static void Add(TableLayoutPanel parent,Control c){c.Dock=DockStyle.Top;c.Margin=new Padding(0,2,0,2);parent.Controls.Add(c);}
        void Field(TableLayoutPanel parent,string label,Control c){var row=new TableLayoutPanel{AutoSize=true,ColumnCount=1};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left});c.Dock=DockStyle.Fill;row.Controls.Add(c);Add(parent,row);rows[c]=row;}
        void Dimension(TableLayoutPanel parent,string key,string label,TextBox box){
            var row=new TableLayoutPanel{AutoSize=true,ColumnCount=2};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));var caption=new Label{Text=label,AutoSize=true};row.Controls.Add(caption,0,0);row.SetColumnSpan(caption,2);box.Dock=DockStyle.Fill;row.Controls.Add(box,0,1);
            var button=new Button{Text="拾取直边",Dock=DockStyle.Fill};button.Click+=(s,e)=>{if(current==null)return;if(current.dimension_references.ContainsKey(key)){current.dimension_references.Remove(key);LoadDimensionSources();Schedule();}else{armed=-1;armedDimension=key;status.Text="请选择直边作为 "+label;BeginSelection?.Invoke();}};row.Controls.Add(button,1,1);Add(parent,row);dimensionRows[key]=row;edgeButtons[key]=button;
        }
        static T Clone<T>(T value){var s=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};return s.Deserialize<T>(s.Serialize(value));}
        sealed class Choice{public string Key,Label;public override string ToString()=>Label;}
        static double Number(TextBox box,double scale){double value;if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value))throw new InvalidOperationException("请输入有限数字。");return value*scale;}
        static void Values(TextBox[] fields,double[] values,double scale){for(int i=0;i<fields.Length;i++)fields[i].Text=(i<values.Length?values[i]*scale:0).ToString("G12",CultureInfo.InvariantCulture);}
        void Visible(Control c,bool visible){rows[c].Visible=visible;}
        void RefreshNames(){loading=true;int selected=list.SelectedIndex;for(int i=0;i<list.Items.Count;i++)list.Items[i]=list.Items[i];list.SelectedIndex=selected;loading=false;}
        void RefreshList(CollisionGeometry select=null){loading=true;displayedLink=(string)link.SelectedItem;list.Items.Clear();foreach(var g in draft.collision.geometries.Where(g=>g.link==displayedLink||(!string.IsNullOrEmpty(g.link_id)&&!linkIds.Values.Contains(g.link_id))))list.Items.Add(g);string m=draft.collision.Mode(linkIds[displayedLink]);mode.SelectedIndex=m=="primitive"?1:m=="none"?2:0;list.SelectedItem=select??list.Items.Cast<CollisionGeometry>().FirstOrDefault();current=list.SelectedItem as CollisionGeometry;loading=false;LoadCurrent();}
        void LoadCurrent(){JointEditorControl.PreserveScroll(this,LoadCurrentCore);}
  void LoadCurrentCore(){
            loading=true;armed=-1;armedDimension=null;definition.Items.Clear();
            var choices=new List<Choice>{new Choice{Key="manual",Label="手动位姿与尺寸"},new Choice{Key="frame",Label="中心坐标系 + 尺寸"},new Choice{Key="center",Label="中心点 + link 方向"},new Choice{Key="center_frame",Label="中心点 + 方向坐标系"}};
            if(current!=null){
                current.dimension_references=current.dimension_references??new Dictionary<string,CollisionReference>();
                if(current.type=="box"){choices.Add(new Choice{Key="corner_frame",Label="角点坐标系 + 尺寸"});choices.Add(new Choice{Key="rectangle_face",Label="矩形面 + 厚度"});}
                if(current.type=="sphere"){choices.Add(new Choice{Key="radius_points",Label="球心 + 球面一点"});choices.Add(new Choice{Key="sphere_face",Label="选择球面"});}
                if(current.type=="cylinder"||current.type=="capsule")choices.Add(new Choice{Key="endpoints",Label=current.type=="capsule"?"两端球心 + 半径":"两端面中心点 + 半径"});
                if(current.type=="cylinder"){choices.Add(new Choice{Key="end_frame",Label="端面圆心坐标系 + 拉伸轴"});choices.Add(new Choice{Key="cylinder_face",Label="圆柱面 + 轴向范围点"});}
                if(current.type=="capsule")choices.Add(new Choice{Key="frame_total",Label="坐标系 + 半径 + 总长度"});
                definition.Items.AddRange(choices.ToArray());shape.SelectedItem=current.type;definition.SelectedItem=choices.First(c=>c.Key==current.definition);name.Text=current.name;
                Values(size,current.size,1000);Values(xyz,current.xyz,1000);Values(rpy,current.rpy,180/Math.PI);Values(offset,current.offset_xyz,1000);Values(rotation,current.offset_rpy,180/Math.PI);
                total.Text=(current.length_input*1000).ToString("G12",CultureInfo.InvariantCulture);thickness.Text=(current.thickness*1000).ToString("G12",CultureInfo.InvariantCulture);extrusion.SelectedIndex=current.extrusion==1?1:current.extrusion==-1?2:0;
                axis.SelectedIndex=current.axis;axisSign.SelectedIndex=current.axis_sign==1?0:1;for(int i=0;i<3;i++)signs[i].SelectedIndex=current.corner_signs[i]==1?0:1;
            }else{name.Text="";}
            foreach(var row in rows.Values)row.Visible=current!=null;foreach(var row in dimensionRows.Values)row.Visible=false;activeDimensions.Clear();
            rows[link].Visible=rows[mode].Visible=rows[pairA].Visible=rows[pairB].Visible=true;for(int i=0;i<3;i++)picks[i].Visible=false;
            referenceInfo.Text=current==null?"先添加或选择一个几何体。":string.Join("\n",current.references.Select((r,i)=>(i+1)+": "+r.label));
            if(current!=null){
                string method=current.definition;bool manual=method=="manual";bool sphere=current.type=="sphere";
                string[] labels=method=="manual"?new string[0]:method=="center_frame"?new[]{"拾取中心点","拾取方向坐标系"}:method=="radius_points"?new[]{"拾取球心","拾取球面上一点"}:method=="endpoints"?new[]{"拾取起点","拾取终点"}:method=="cylinder_face"?new[]{"拾取圆柱面","拾取轴向范围点 1","拾取轴向范围点 2"}:method=="rectangle_face"?new[]{"拾取矩形面"}:method=="sphere_face"?new[]{"拾取球面"}:method=="center"?new[]{"拾取中心点"}:new[]{"拾取坐标系"};
                for(int i=0;i<labels.Length;i++){picks[i].Text=labels[i];picks[i].Visible=true;}
                for(int i=0;i<3;i++){Visible(xyz[i],manual);Visible(rpy[i],manual&&!sphere);Visible(offset[i],!manual);Visible(rotation[i],!manual&&!sphere);Visible(signs[i],method=="corner_frame");}
                Visible(axis,method=="end_frame");Visible(axisSign,method=="end_frame");Visible(extrusion,method=="rectangle_face");
                bool computed=method=="rectangle_face"||method=="sphere_face"||method=="radius_points"||method=="cylinder_face";
                int count=current.type=="box"?3:sphere?1:2;
                for(int i=0;i<count;i++){
                    DimensionVisible("size"+i,!computed&&!(i==1&&(method=="endpoints"||method=="frame_total")));
                    ((Label)dimensionRows["size"+i].Controls[0]).Text=current.type=="box"?new[]{"长 X mm","宽 Y mm","高 Z mm"}[i]:i==0?"半径 mm":current.type=="capsule"?"圆柱段长度 mm":"高度 mm";
                    edgeButtons["size"+i].Visible=current.type=="box"||i==1;
                }
                DimensionVisible("total",method=="frame_total");DimensionVisible("thickness",method=="rectangle_face");LoadDimensionSources();
            }
            shape.Enabled=definition.Enabled=current!=null;loading=false;Schedule();
        }
        void DimensionVisible(string key,bool visible){dimensionRows[key].Visible=visible;if(visible)activeDimensions.Add(key);}
        void LoadDimensionSources(){if(current==null)return;foreach(var pair in edgeButtons){bool bound=current.dimension_references.ContainsKey(pair.Key);pair.Value.Text=bound?"改为手动":"拾取直边";var box=pair.Key=="total"?total:pair.Key=="thickness"?thickness:size[int.Parse(pair.Key.Substring(4))];box.ReadOnly=bound;pair.Value.Tag=bound?current.dimension_references[pair.Key].label:null;}}
        public void CaptureSelected(){if(current==null||(armed<0&&armedDimension==null))return;Guard(()=>{var reference=service.CaptureSelection();if(armedDimension!=null){service.ReferenceEdgeLength(reference);current.dimension_references[armedDimension]=reference;armedDimension=null;LoadDimensionSources();}else{if(armed>current.references.Count)throw new InvalidOperationException("请按顺序拾取参考。");if(armed==current.references.Count)current.references.Add(reference);else current.references[armed]=reference;armed=-1;}referenceInfo.Text=string.Join("\n",current.references.Select((r,i)=>(i+1)+": "+r.label));Schedule();});}
        void Schedule(){if(loading)return;timer.Stop();timer.Start();}
        void ReadCurrent(){ReadCurrent(false);}
        void ReadCurrent(bool allowUnresolvedReferences){
            if(current==null)return;var candidate=Clone(current);candidate.name=name.Text.Trim();
            for(int i=0;i<candidate.size.Length;i++)if(activeDimensions.Contains("size"+i)&&!candidate.dimension_references.ContainsKey("size"+i))candidate.size[i]=Number(size[i],.001);
            if(candidate.definition=="manual"){candidate.xyz=xyz.Select(b=>Number(b,.001)).ToArray();candidate.rpy=candidate.type=="sphere"?new double[3]:rpy.Select(b=>Number(b,Math.PI/180)).ToArray();}
            else{candidate.offset_xyz=offset.Select(b=>Number(b,.001)).ToArray();candidate.offset_rpy=candidate.type=="sphere"?new double[3]:rotation.Select(b=>Number(b,Math.PI/180)).ToArray();}
            if(candidate.definition=="frame_total"&&!candidate.dimension_references.ContainsKey("total"))candidate.length_input=Number(total,.001);
            if(candidate.definition=="rectangle_face"){if(!candidate.dimension_references.ContainsKey("thickness"))candidate.thickness=Number(thickness,.001);candidate.extrusion=extrusion.SelectedIndex==1?1:extrusion.SelectedIndex==2?-1:0;}
            candidate.axis=axis.SelectedIndex<0?2:axis.SelectedIndex;candidate.axis_sign=axisSign.SelectedIndex==1?-1:1;candidate.corner_signs=signs.Select(c=>c.SelectedIndex==1?-1:1).ToArray();
            candidate.Validate();
            // Navigation retains an unfinished draft. Resolve a copy so a failed CAD
            // reference cannot overwrite its last valid pose or partially update dimensions.
            var resolved=Clone(candidate);
            try{service.ResolveCollision(resolved);candidate=resolved;}
            catch(Exception){if(!allowUnresolvedReferences)throw;}
            current.size=candidate.size;current.xyz=candidate.xyz;current.rpy=candidate.rpy;current.offset_xyz=candidate.offset_xyz;current.offset_rpy=candidate.offset_rpy;current.length_input=candidate.length_input;current.thickness=candidate.thickness;current.extrusion=candidate.extrusion;current.axis=candidate.axis;current.axis_sign=candidate.axis_sign;current.corner_signs=candidate.corner_signs;
            loading=true;for(int i=0;i<current.size.Length;i++)if(current.dimension_references.ContainsKey("size"+i))size[i].Text=(current.size[i]*1000).ToString("G12",CultureInfo.InvariantCulture);if(current.dimension_references.ContainsKey("total"))total.Text=(current.length_input*1000).ToString("G12",CultureInfo.InvariantCulture);if(current.dimension_references.ContainsKey("thickness"))thickness.Text=(current.thickness*1000).ToString("G12",CultureInfo.InvariantCulture);loading=false;
        }
        string previewKey;
        void UpdatePreview(bool cadChanged){
            cadChanged|=refreshReferences;refreshReferences=false;
            string error=null;try{ReadCurrent();}catch(Exception ex){error=ex.Message;}
            if(cadChanged)foreach(var g in draft.collision.geometries.Where(g=>g!=current))try{service.ResolveCollision(g);}catch(Exception ex){error=ex.Message;}
            string key=service.CollisionRevision+new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{draft.collision.geometries,selected=current?.id});
            try{if(show.Checked){if(cadChanged||key!=previewKey){preview.Show(draft.collision.geometries,current?.id);previewKey=error==null?key:null;}}else{preview.Clear();previewKey=null;}}catch(Exception ex){error=ex.Message;}
            status.Text=error??"预览已更新；保存配置后仍需保存装配文件。";
        }
        void RefreshPairs(){pairs.Items.Clear();foreach(var p in draft.collision.allowed_pairs)pairs.Items.Add(p);if(pairs.Items.Count>0)pairs.SelectedIndex=0;}
        void Guard(Action action){try{action();}catch(Exception ex){status.Text=ex.Message;}}
        public void Save(){
            if(solverInvalid.Count>0)throw new InvalidOperationException("请修正求解参数中的无效数字。");draft.ValidateSolver();timer.Stop();if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration)throw new InvalidOperationException("SW Configuration 已切换，请重新进入碰撞配置。");ReadCurrent();
            var frames=service.LinkTransforms();var names=new HashSet<string>();foreach(var g in draft.collision.geometries){service.ResolveCollision(g);if(!names.Add(g.name))throw new InvalidOperationException("碰撞几何体名称重复："+g.name);}
            foreach(var m in draft.collision.link_modes_by_id){var owner=linkIds.SingleOrDefault(x=>x.Value==m.Key);if(owner.Key==null)throw new InvalidOperationException("碰撞模式 link ID 已失效："+m.Key);if(m.Value=="primitive"&&!draft.collision.geometries.Any(g=>g.link==owner.Key))throw new InvalidOperationException("简单几何体模式至少需要一个几何体。");}
            foreach(var p in draft.collision.allowed_pairs){if(!frames.ContainsKey(p.link1)||!frames.ContainsKey(p.link2))throw new InvalidOperationException("允许碰撞对的 link 已失效。");if(draft.collision.Mode(linkIds[p.link1])=="none"||draft.collision.Mode(linkIds[p.link2])=="none")throw new InvalidOperationException("允许碰撞对不能引用无碰撞 link。");}
            draft.collision.disable_internal=disable.Checked;var previous=service.Project;service.Project=Clone(draft);try{service.Save();}catch{service.Project=previous;throw;}status.Text="已写入装配配置节点，请保存 .sldasm。";
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();cadTimer.Stop();cadTimer.Dispose();preview.Dispose();}base.Dispose(disposing);}
    }
}
