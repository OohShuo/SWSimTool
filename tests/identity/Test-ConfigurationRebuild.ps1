param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48',[switch]$ExpectKnownFailure)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $root ('build/'+$Payload)
$sdk='D:/sw/sw2025/SOLIDWORKS'
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/MathNet.Numerics.dll","$root/build/bin/SWSimTool.Tests/Release/net48/Moq.dll","$root/build/bin/SWSimTool.Tests/Release/net48/Castle.Core.dll","$sdk/SolidWorks.Interop.sldworks.dll",'System.Core','System.Xml','System.Runtime.Serialization','System.Web.Extensions','System.Windows.Forms')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
[SWSimTool.Utilities.Logger].GetField('Initialized',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Reflection;using System.Runtime.Serialization;using System.Linq;using Moq;using SolidWorks.Interop.sldworks;using SWSimTool.Simulation;using SWSimTool.URDF;using SWSimTool.URDFExport;using SWSimTool.UI;using System.Windows.Forms;
public static class ConfigurationRebuildTest {
 public static void Run(bool knownFailure) {
  var c=new Mock<Configuration>();c.SetupGet(x=>x.Name).Returns("test");var cm=new Mock<ConfigurationManager>();cm.SetupGet(x=>x.ActiveConfiguration).Returns(c.Object);
  string raw=null;bool present=true;
  bool rejectNextWrite=false;var p=new Mock<Parameter>();p.Setup(x=>x.GetStringValue()).Returns(()=>raw);p.Setup(x=>x.SetStringValue2(It.IsAny<string>(),It.IsAny<int>(),It.IsAny<string>())).Returns((string v,int scope,string name)=>{if(rejectNextWrite){rejectNextWrite=false;return false;}raw=v;return true;});
  var a=new Mock<SolidWorks.Interop.sldworks.Attribute>();a.Setup(x=>x.GetName()).Returns(SimulationStorage.NodeName);a.Setup(x=>x.GetParameter("data")).Returns(p.Object);
  var f=new Mock<Feature>();f.Setup(x=>x.GetTypeName2()).Returns("Attribute");f.Setup(x=>x.GetSpecificFeature2()).Returns(a.Object);
  var fm=new Mock<FeatureManager>();fm.Setup(x=>x.GetFeatures(true)).Returns(()=>present?new object[]{f.Object}:new object[0]);
  var m=new Mock<ModelDoc2>();var nodeExtension=new Mock<ModelDocExtension>();nodeExtension.Setup(x=>x.GetPersistReference3(It.IsAny<object>())).Returns(new byte[]{9,8,7});m.SetupGet(x=>x.Extension).Returns(nodeExtension.Object);m.SetupGet(x=>x.FeatureManager).Returns(fm.Object);m.SetupGet(x=>x.ConfigurationManager).Returns(cm.Object);m.Setup(x=>x.GetConfigurationNames()).Returns(new[]{"test"});m.Setup(x=>x.GetConfigurationByName("test")).Returns(c.Object);m.Setup(x=>x.GetPathName()).Returns("isolated.SLDASM");
  Func<LinkNode> tree=()=>{var r=new Link(null);r.Name="base";var n=new LinkNode(r);var l=new Link(r);l.Name="pitch1";l.Joint.Name="pitch1_joint";n.Nodes.Add(new LinkNode(l));return n;};
  var oldTree=tree();var oldProject=new SimulationProject();oldProject.attachments.Add(new Attachment{name="site_pitch1",link="pitch1",type="point"});StableReferences.Normalize(oldProject,oldTree);
  var d=new SimulationStorage.Document();d.configurations["test"]=new SimulationStorage.Entry{urdf_xml=ConfigurationSerialization.WriteTree(oldTree),simulation=oldProject};raw=ExportFingerprint.Serializer().Serialize(d);
  var helper=(ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));helper.ActiveSWModel=m.Object;
  var oldPage=new AttachmentService(helper);
  present=false;if(SimulationStorage.Load(m.Object)!=null)throw new Exception("Deleted node still loaded");
  var newTree=tree();d=new SimulationStorage.Document();d.configurations["test"]=new SimulationStorage.Entry{urdf_xml=ConfigurationSerialization.WriteTree(newTree),simulation=new SimulationProject()};present=true;raw=ExportFingerprint.Serializer().Serialize(d);
  var savedBefore=raw;bool rejected=false;try{oldPage.Save();}catch(InvalidDataException){rejected=true;}
  var now=SimulationStorage.Load(m.Object);bool contaminated=now.attachments.Any(x=>x.link_id==oldProject.attachments[0].link_id);
  if(knownFailure){if(!contaminated||rejected)throw new Exception("Known failure did not reproduce");Console.WriteLine("REPRODUCED: old page writes deleted link ID into rebuilt configuration");}
  else {if(!rejected||raw!=savedBefore||contaminated)throw new Exception("Old page contaminated replacement node");Console.WriteLine("PASS: stale save rejected without modifying replacement configuration");}
  if(now.attachments.Count>0){if(StableReferences.LinkNames(newTree).ContainsKey(now.attachments[0].link_id))throw new Exception("Dangling ID unexpectedly matched");Console.WriteLine("Unknown stable reference: "+now.attachments[0].link_id+" (site_pitch1 parent link pitch1)");}
  var duplicate=tree();var extra=new Link(duplicate.Link);extra.Name="other";extra.Joint.Name="pitch1_joint";duplicate.Nodes.Add(new LinkNode(extra));bool duplicateRejected=false;
  try{JointDescriptor.FromTree(duplicate).ToDictionary(x=>x.name);}catch(Exception e){duplicateRejected=true;Console.WriteLine("PASS duplicate joint-name diagnostic: "+e.Message);}
  if(!duplicateRejected)throw new Exception("Duplicate joint-name case did not fail");
  if(!knownFailure){
   var freshPage=new AttachmentService(helper);var anotherPage=new AttachmentService(helper);
   string originalRaw=raw;var shared=freshPage.Project;var sharedTree=freshPage.Editing.Tree;
   freshPage.BeginPage();freshPage.Project.solver=new SolverSettings{enabled=true,timestep=.002};freshPage.Project.attachments.Add(new Attachment{name="cancelled",link="pitch1"});
   if(anotherPage.Project.attachments.Count!=0||!Object.ReferenceEquals(shared,anotherPage.Project))throw new Exception("Page edits leaked into shared draft");
   bool secondRejected=false;try{anotherPage.BeginPage();}catch(InvalidOperationException){secondRejected=true;}
   if(!secondRejected)throw new Exception("Concurrent page accepted");freshPage.EndPage();
   if(raw!=originalRaw||anotherPage.Project.attachments.Count!=0)throw new Exception("Cancellation changed persisted or shared state");
   freshPage.BeginPage();freshPage.Project.solver=new SolverSettings{enabled=true,timestep=.002};freshPage.Project.attachments.Add(new Attachment{name="unsaved",link="pitch1"});
   int flushed=0;freshPage.PageDraft.Flush=()=>flushed++;freshPage.FlushPage();if(flushed!=1)throw new Exception("Active inputs not collected");
   if(anotherPage.Project.attachments.Count!=0)throw new Exception("Collection committed draft prematurely");
   bool staleRebuild=false;try{oldPage.RebuildCurrentDraft();}catch(InvalidDataException){staleRebuild=true;}
   if(!staleRebuild||raw!=savedBefore)throw new Exception("Stale rebuild changed new configuration");
   Console.WriteLine("PASS: complete unsaved draft shared; stale rebuild rejected before CAD or writes");
   rejectNextWrite=true;bool failedSave=false;try{freshPage.Save();}catch(IOException){failedSave=true;}
   if(!failedSave||raw!=savedBefore||anotherPage.Project.attachments.Count!=0||freshPage.Project.attachments.Count!=1||freshPage.Project.solver.timestep!=.002)throw new Exception("Failed save changed shared or persisted data, or discarded page input");
   freshPage.Save();freshPage.EndPage();bool lateSaveRejected=false;try{freshPage.Save();}catch(InvalidDataException){lateSaveRejected=true;}if(!lateSaveRejected)throw new Exception("Closed page service accepted late save callback");
   var reopened=SimulationStorage.Load(m.Object);
   string newParent=freshPage.Editing.Tree.Children[0].StableId;
   if(reopened.attachments.Single().link_id!=newParent||reopened.attachments.Single().link_id==oldProject.attachments[0].link_id)throw new Exception("Fresh configuration reused deleted identity");
   SimulationStorage.Invalidate(m.Object);ConfigurationEditingContext.Forget(m.Object);
   var coldPage=new AttachmentService(helper);
   if(coldPage.Project.attachments.Single().link_id!=newParent||coldPage.Project.solver.timestep!=.002)throw new Exception("Cold reopen lost saved draft");
   SimulationSession.ExportSucceeded(m.Object);
   coldPage.BeginPage();coldPage.Project.solver.timestep=.0025;var solverSave=coldPage.Save();
   var dirty=SimulationSession.Dirty(m.Object);
   if(!solverSave.RefreshSucceeded||dirty!=(SimulationDirtyFlags.Solver|SimulationDirtyFlags.Mjcf))throw new Exception("Timestep-only page save incorrectly marked source/mesh dirty: "+dirty);
   var identityMethod=typeof(AttachmentService).Assembly.GetType("SWSimTool.Simulation.SolidWorksAttributeDocumentStore").GetMethod("NodeIdentity",BindingFlags.Static|BindingFlags.NonPublic);
   nodeExtension.Setup(x=>x.GetPersistReference3(It.IsAny<object>())).Returns((object)null);bool identityRejected=false;
   try{identityMethod.Invoke(null,new object[]{m.Object});}catch(TargetInvocationException e){identityRejected=e.InnerException is InvalidDataException;}
   if(!identityRejected)throw new Exception("Unavailable CAD persistent identity silently accepted");
   nodeExtension.Setup(x=>x.GetPersistReference3(It.IsAny<object>())).Returns(new byte[]{9,8,7});
   Console.WriteLine("PASS: timestep-only page save marks only solver/MJCF; unavailable node identity fails closed");
   coldPage.Project.solver.timestep=.003;
   m.Setup(x=>x.SetSaveFlag()).Throws(new IOException("injected post-commit refresh failure"));
   var committed=coldPage.Save();
   if(!committed.Committed||committed.RefreshSucceeded||!committed.Message.Contains("已提交")||SimulationStorage.Load(m.Object).solver.timestep!=.003)throw new Exception("Post-commit failure reported unsaved or lost committed data");
   var committedRaw=raw;bool retryBlocked=false;try{coldPage.Save();}catch(InvalidDataException){retryBlocked=true;}
   if(!retryBlocked||raw!=committedRaw)throw new Exception("Post-commit failure accepted retry");
   m.Setup(x=>x.SetSaveFlag());coldPage.EndPage();coldPage=new AttachmentService(helper);
   var outcome=new ConfigurationCommitResult();typeof(ConfigurationCommitResult).GetMethod("Run",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(outcome,new object[]{(Action)(()=>{throw new IOException("injected page close failure");})});
   if(!outcome.Committed||outcome.RefreshSucceeded)throw new Exception("Page close failure lost committed outcome");
   Console.WriteLine("PASS: committed refresh/close failures are warnings; old page cannot retry");
   var savedRaw=raw;c.SetupGet(x=>x.Name).Returns("other");bool switched=false;try{coldPage.Save();}catch(InvalidDataException){switched=true;}
   if(!switched||raw!=savedRaw)throw new Exception("SW configuration switch accepted old page");
   c.SetupGet(x=>x.Name).Returns("test");ConfigurationSession.Invalidate(m.Object,true);bool closed=false;try{coldPage.Save();}catch(InvalidDataException){closed=true;}
   if(!closed||raw!=savedRaw)throw new Exception("Closed document accepted old page");
   Console.WriteLine("PASS: fresh save/cold reopen retain new IDs; configuration switch and document close reject stale saves");
   var uiTree=tree();uiTree.UpdateLinkTree(null);var before=uiTree.Link.Children[0];string beforeName=before.Name;
   var missingId=typeof(Link).GetField("stableId",BindingFlags.NonPublic|BindingFlags.Instance);missingId.SetValue(uiTree.Link,null);
   ConfigurationSerialization.WriteTree(uiTree);StableReferences.ValidateTree(uiTree);
   if(missingId.GetValue(uiTree.Link)!=null)throw new Exception("Read-only serialization or validation materialized input identity");
   foreach(var property in typeof(ConfigurationEditingContext).GetProperties())if(typeof(TreeNode).IsAssignableFrom(property.PropertyType)||typeof(Control).IsAssignableFrom(property.PropertyType)||typeof(Delegate).IsAssignableFrom(property.PropertyType))throw new Exception("Business context holds UI ownership");
   var clone=(LinkNode)uiTree.Clone();
   if(!Object.ReferenceEquals(clone.Link.Children[0],((LinkNode)clone.Nodes[0]).Link)||!Object.ReferenceEquals(clone.Link.Children[0].Parent,clone.Link))throw new Exception("Clone has two business trees");
   ((LinkNode)clone.Nodes[0]).Link.Name="clone_changed";
   if(before.Name!=beforeName)throw new Exception("Clone shares mutable business objects");
   using(var tv=new TreeView()){tv.Nodes.Add(uiTree);var child=(LinkNode)uiTree.Nodes[0];var grand=new Link(child.Link);grand.Name="grand";child.Nodes.Add(new LinkNode(grand));
    LinkNode.Move(child,(LinkNode)child.Nodes[0]);LinkNode.Move(uiTree,child);
    if(uiTree.Nodes.Count!=1||child.Nodes.Count!=1||child.Parent!=uiTree)throw new Exception("Invalid drag changed topology");
    var snapshot=uiTree.RebuildLink();if(!Object.ReferenceEquals(before,uiTree.Link.Children[0])||snapshot.Children.Count!=1||!Object.ReferenceEquals(snapshot.Children[0].Parent,snapshot))throw new Exception("Snapshot mutated input or wrong parent");
    ConfigurationSerialization.WriteTree(uiTree);if(!Object.ReferenceEquals(before,uiTree.Link.Children[0])||before.Name!=beforeName)throw new Exception("Serialization mutated input");
    var other=new Link(uiTree.Link);other.Name="other";var otherNode=new LinkNode(other);uiTree.Nodes.Add(otherNode);LinkNode.Move(child,otherNode);
    if(child.Parent!=otherNode||child.Link.Parent!=otherNode.Link||child.IsBaseNode)throw new Exception("Legal drag parent state inconsistent");
   }
   var limited=new Joint{Name="limited",Type="revolute"};limited.Parent.Name="base";limited.Child.Name="child";limited.Limit.SetInputs("-1.2","1.4","7","2.5");
   using(var limits=new UrdfJointLimitControl()){
    limits.LoadJoint(limited,false);limits.SetType("fixed");limits.Commit(limited);limits.SetType("revolute");limits.Commit(limited);
    if(limited.Limit.Lower!=-1.2||limited.Limit.Upper!=1.4||limited.Limit.Effort!=7)throw new Exception("Type switch discarded hidden limit inputs");
   }
   ((LinkNode)newTree.Nodes[0]).Link.Joint.SetElement(limited);

   var persistedLimits=ConfigurationSerialization.ReadTree(ConfigurationSerialization.WriteTree(newTree),1.4);
   if(((LinkNode)persistedLimits.Nodes[0]).Link.Joint.Limit.Velocity!=2.5)throw new Exception("Tree persistence lost limit settings");
   Func<Joint,string> xml=j=>{var buffer=new System.Text.StringBuilder();using(var writer=System.Xml.XmlWriter.Create(buffer,new System.Xml.XmlWriterSettings{OmitXmlDeclaration=true}))j.WriteURDF(writer);return buffer.ToString();};
   if(!xml(limited).Contains("lower=\"-1.2\""))throw new Exception("Revolute output lost bounds");
   limited.Type="fixed";if(xml(limited).Contains("<limit"))throw new Exception("Fixed output incorrectly writes stored limit");
   limited.Type="continuous";var continuousXml=xml(limited);if(continuousXml.Contains("lower=")||!continuousXml.Contains("effort=\"7\""))throw new Exception("Continuous output must retain effort, omit bounds");
   if(limited.Limit.Lower!=-1.2)throw new Exception("URDF generation modified stored bounds");
   var exportCopy=AssemblyExportPreparation.CopyTree(newTree.Snapshot());((LinkNode)exportCopy.Nodes[0]).Link.Joint.Limit.SetInputs("-2","2","8","4");
   if(((LinkNode)newTree.Nodes[0]).Link.Joint.Limit.Effort==8)throw new Exception("Export preparation shares editable joint objects");
   var settingsProject=new SimulationProject{urdf_export=new UrdfExportSettings{inertia=false,geometry=true,kinematics=false,limits=false}};
   var settingsCopy=ConfigurationEditingContext.CopyProject(settingsProject);
   if(settingsCopy.urdf_export.inertia||settingsCopy.urdf_export.kinematics||!settingsCopy.urdf_export.geometry)throw new Exception("Export business settings lost on persistence copy");
   bool badLimit=false;try{limited.Limit.SetInputs("5","-1","7","2.5");}catch(ArgumentException){badLimit=true;}if(!badLimit||limited.Limit.Lower!=-1.2)throw new Exception("Invalid limit input partially changed joint");
   Console.WriteLine("PASS: joint limits round-trip, hidden-input retention, fixed/continuous XML and atomic numeric validation");
   Console.WriteLine("PASS: isolated page cancel/confirm; clone coherence; pure snapshot/serialization; legal and illegal drag");
   // A constructor failure after claiming ownership must allow an immediate retry.
   var failConfig=new Mock<Configuration>();failConfig.SetupGet(x=>x.Name).Returns("fail");var failManager=new Mock<ConfigurationManager>();failManager.SetupGet(x=>x.ActiveConfiguration).Returns(failConfig.Object);
   var failModel=new Mock<ModelDoc2>();failModel.SetupGet(x=>x.ConfigurationManager).Returns(failManager.Object);failModel.SetupGet(x=>x.FeatureManager).Returns(new Mock<FeatureManager>().Object);
   var failHelper=(ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));failHelper.ActiveSWModel=failModel.Object;var failureService=new AttachmentService(failHelper);
   bool constructorFailed=false;try{new JointEditorControl(failureService,null);}catch{constructorFailed=true;}
   if(!constructorFailed||ConfigurationPageDraft.Active(failModel.Object)!=null)throw new Exception("Initialization failure leaked active page");
   using(var retryEditor=new JointEditorControl(failureService,new System.Collections.Generic.List<JointDescriptor>())){
    var owned=failureService.PageDraft;bool secondFailed=false;try{new JointEditorControl(failureService,null);}catch{secondFailed=true;}
    if(!secondFailed||!Object.ReferenceEquals(owned,ConfigurationPageDraft.Active(failModel.Object)))throw new Exception("Rejected repeated page released original page");
   }
   var undoPage=new AttachmentService(failHelper);undoPage.BeginPage();
   var history=(SWSimTool.SW.AssemblyEventHandler)FormatterServices.GetUninitializedObject(typeof(SWSimTool.SW.AssemblyEventHandler));typeof(SWSimTool.SW.DocumentEventHandler).GetField("document",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(history,failModel.Object);
   history.OnHistoryChanged();bool undoRejected=false;try{undoPage.Save();}catch(InvalidDataException){undoRejected=true;}undoPage.EndPage();
   if(!undoRejected)throw new Exception("Undo/redo callback left old page writable");
   failureService=new AttachmentService(failHelper);
   failureService.BeginPage();var released=failureService.PageDraft;failureService.EndPage();bool releasedRejected=false;try{released.Collect();}catch(InvalidDataException){releasedRejected=true;}
   if(!releasedRejected)throw new Exception("Disposed page callback still accepted");
   Console.WriteLine("PASS: initialization failure retries; repeated open retains original owner; disposed callback rejected");


  }
 }
}
'@
[ConfigurationRebuildTest]::Run($ExpectKnownFailure.IsPresent)
