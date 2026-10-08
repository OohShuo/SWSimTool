/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using SWSimTool.UI;
using SWSimTool.URDFExport;
using SWSimTool.Utilities;
using System;
using System.Collections;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SWSimTool.Simulation;

namespace SWSimTool.SW
{
    // Adding a new line
    //
    /// <summary>
    /// Summary description for SWSimTool.
    /// </summary>
    [Guid("974a302b-3966-4e45-a5a5-1c24c26b7faa"), ComVisible(true)]
    [SwAddin(
        Description = "SolidWorks robot configuration and MuJoCo / URDF export",
        Title = "SWSimTool",
        LoadAtStartup = true
        )]
    public class SwAddin : ISwAddin
    {
        #region Static Variables

        private static readonly log4net.ILog logger = Logger.GetLogger();

        #endregion Static Variables

        #region Local Variables

        private int add_in_id_ = 0;

        private const int MuJoCoCommandGroupId = 4935;
        private MuJoCoToolsForm muJoCoTools;
        public const int mainCmdGroupID = 5;
        public const int mainItemID1 = 0;
        public const int mainItemID2 = 1;
        public const int mainItemID3 = 2;
        public const int flyoutGroupID = 91;

        #region Event Handler Variables

        private SldWorks SwEventPtr = null;

        #endregion Event Handler Variables

        // Public Properties
        public ISldWorks SwApp { get; private set; } = null;

        public ICommandManager CmdMgr { get; private set; } = null;

        public Hashtable OpenDocs { get; private set; } = new Hashtable();

        #endregion Local Variables

        #region SolidWorks Registration

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            #region Get Custom Attribute: SwAddinAttribute

            SwAddinAttribute SWattr = null;
            Type type = typeof(SwAddin);

            foreach (System.Attribute attr in type.GetCustomAttributes(false))
            {
                if (attr is SwAddinAttribute)
                {
                    SWattr = attr as SwAddinAttribute;
                    break;
                }
            }

            #endregion Get Custom Attribute: SwAddinAttribute

            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                logger.Info("Registering " + keyname);
                Microsoft.Win32.RegistryKey addinkey = hklm.CreateSubKey(keyname);
                addinkey.SetValue(null, 0);

                addinkey.SetValue("Description", SWattr.Description);
                addinkey.SetValue("Title", SWattr.Title);

                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                logger.Info("Registering " + keyname);
                addinkey = hkcu.CreateSubKey(keyname);
                addinkey.SetValue(
                    null, Convert.ToInt32(SWattr.LoadAtStartup), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (NullReferenceException nl)
            {
                logger.Error("There was a problem registering this dll: SWattr is null. \n\"" +
                    nl.Message + "\"", nl);
                // MessageBox.Show("There was a problem registering this dll: SWattr is null. \n\"" +
                //     nl.Message + "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
            catch (Exception e)
            {
                logger.Error(e.Message);
                // MessageBox.Show("There was a problem registering the function: \n\"" + e.Message +
                //    "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                logger.Info("Unregistering " + keyname);
                hklm.DeleteSubKey(keyname);

                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                logger.Info("Unregistering " + keyname);
                hkcu.DeleteSubKey(keyname);
            }
            catch (NullReferenceException nl)
            {
                logger.Error("There was a problem unregistering this dll: " + nl.Message);
                MessageBox.Show("There was a problem unregistering this dll: \n\"" +
                    nl.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
            catch (Exception e)
            {
                logger.Error("There was a problem unregistering this dll: " + e.Message);
                MessageBox.Show("There was a problem unregistering this dll: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        #endregion SolidWorks Registration

        #region ISwAddin Implementation

        public SwAddin()
        {
            Logger.Setup();
        }

        private void ExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            logger.Warn("Exception encountered in Assembly export form", e.Exception);
        }

        private void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logger.Error("Unhandled exception in Assembly Export form\nEmail your maintainer " +
                "with the log file found at " +
                Logger.GetFileName(), (Exception)e.ExceptionObject);
        }

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            logger.Info("Attempting to connect to SW");
            SwApp = (ISldWorks)ThisSW;
            add_in_id_ = cookie;

            //Setup callbacks
            logger.Info("Setting up callbacks");
            SwApp.SetAddinCallbackInfo(0, this, add_in_id_);

            #region Setup the Command Manager
            logger.Info("Setting up command manager");
            CmdMgr = SwApp.GetCommandManager(cookie);

            logger.Info("Adding command manager");
            AddCommandMgr();

            #endregion Setup the Command Manager

            #region Setup the Event Handlers
            logger.Info("Adding event handlers");
            SwEventPtr = (SldWorks)SwApp;
            OpenDocs = new Hashtable();
            AttachEventHandlers();

            #endregion Setup the Event Handlers

            logger.Info("Connecting plugin to SolidWorks");
            return true;
        }

        public bool DisconnectFromSW()
        {
            if (muJoCoTools != null && !muJoCoTools.IsDisposed) muJoCoTools.Close();
            RemoveCommandMgr();
            DetachEventHandlers();

            Marshal.ReleaseComObject(CmdMgr);
            CmdMgr = null;
            Marshal.ReleaseComObject(SwApp);
            SwApp = null;
            //The addin _must_ call GC.Collect() here in order to retrieve all managed code pointers
            GC.Collect();
            GC.WaitForPendingFinalizers();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            logger.Info("Disconnecting plugin from SolidWorks");
            return true;
        }

        #endregion ISwAddin Implementation

        #region UI Methods

        public void AddCommandMgr()
        {
            try { AddMuJoCoCommand(); }
            catch (Exception error) { logger.Error("Cannot add MuJoCo tools command", error); }
        }
        private void AddMuJoCoCommand()
        {
            int errors=0;
            var group=CmdMgr.CreateCommandGroup2(MuJoCoCommandGroupId,"SWSimTool","机器人配置与仿真导出","SWSimTool",-1,true,ref errors);
            if(group==null)throw new InvalidOperationException("无法创建 SWSimTool 菜单："+errors);
            string directory=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(SwAddin).Assembly.Location),"images");
            string[] icons=Array.ConvertAll(new[]{20,32,40,64,96,128},size=>System.IO.Path.Combine(directory,"ros_logo_"+size+"x"+size+".png"));
            group.IconList=icons;group.MainIconList=icons;
            string[] labels={"URDF 配置","碰撞配置","仿真配置","约束配置","关节配置"};string[] callbacks={"OpenUrdfConfiguration","OpenCollisionConfiguration","OpenSimulationConfiguration","OpenConstraintConfiguration","OpenJointConfiguration"};
            for(int i=0;i<labels.Length;i++)group.AddCommandItem2(labels[i],-1,labels[i],labels[i],0,callbacks[i],"CollisionEnableMethod",i,(int)swCommandItemType_e.swMenuItem|(int)swCommandItemType_e.swToolbarItem);
            group.AddCommandItem2("清空当前插件配置",-1,"备份后清空当前 SolidWorks 配置的插件设置","清空当前插件配置",0,"ResetCurrentConfiguration","CollisionEnableMethod",20,(int)swCommandItemType_e.swMenuItem);
            group.AddCommandItem2("恢复配置备份",-1,"恢复当前文档和配置的插件备份","恢复配置备份",0,"RestoreConfigurationBackup","CollisionEnableMethod",21,(int)swCommandItemType_e.swMenuItem);
            group.HasMenu=true;group.HasToolbar=true;group.Activate();
            string[] exports={"导出 URDF","从当前工程导出 MJCF","从本地 URDF 导出 MJCF","预览已有 MJCF"};
            string[] methods={"OpenUrdfExport","OpenProjectExport","OpenLocalExport","OpenExistingPreview"};
            for(int doc=0;doc<=3;doc++){
                SwApp.RemoveMenu(doc,"Export as URDF@&Tools","");
                for(int i=0;i<exports.Length;i++)if(SwApp.AddMenuItem5(doc,add_in_id_,exports[i]+"@导出与预览@SWSimTool",-1,methods[i],i==1?"CollisionEnableMethod":i==0?"UrdfEnableMethod":"ToolbarEnableMethod",","+exports[i],icons)<0)throw new InvalidOperationException("无法创建导出菜单："+exports[i]);
            }
        }
        public void OpenUrdfConfiguration(){AssemblyURDFExporter();}
        public void ResetCurrentConfiguration(){
            try{
                ModelDoc2 model=SwApp.ActiveDoc;if(model==null)return;
                var session=ConfigurationSession.Capture((SldWorks)SwApp,model);
                string name=model.ConfigurationManager.ActiveConfiguration.Name;
                if(MessageBox.Show("将清空当前文档、SolidWorks 配置“"+name+"”中的 URDF 树、仿真及碰撞设置。\n执行前自动备份；其他 SolidWorks 配置、零件、配合、草图、参考几何体和已导出文件保持不变。\n旧配置页面将失效。是否继续？","清空当前插件配置",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;
                string path=SimulationStorage.ResetCurrent((SldWorks)SwApp,model,session);
                MessageBox.Show("当前插件配置已清空。请重新进入 URDF 配置搭建。\n备份："+path,"SWSimTool");
            }catch(Exception error){MessageBox.Show(error.Message,"重置失败",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        public void RestoreConfigurationBackup(){
            try{
                ModelDoc2 model=SwApp.ActiveDoc;if(model==null)return;var session=ConfigurationSession.Capture((SldWorks)SwApp,model);
                using(var dialog=new OpenFileDialog{Filter="SWSimTool 配置备份|*.swsimtool-backup.json",Title="恢复当前文档、当前 SolidWorks 配置"}){
                    if(dialog.ShowDialog()!=DialogResult.OK)return;
                    if(MessageBox.Show("将恢复备份中的当前插件配置，并先备份现有保存内容。其他 SolidWorks 配置和 CAD 实体保持不变。是否继续？","恢复配置备份",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;
                    string path=SimulationStorage.RestoreCurrent((SldWorks)SwApp,model,session,dialog.FileName);
                    MessageBox.Show("已恢复，请重新进入配置。恢复前备份："+path,"SWSimTool");
                }
            }catch(Exception error){MessageBox.Show(error.Message,"恢复失败",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        public int UrdfEnableMethod(){ModelDoc2 model=SwApp.ActiveDoc;return model!=null&&(model.GetType()==(int)swDocumentTypes_e.swDocPART||model.GetType()==(int)swDocumentTypes_e.swDocASSEMBLY)?1:0;}
        public void OpenUrdfExport(){ModelDoc2 model=SwApp.ActiveDoc;if(model!=null&&model.GetType()==(int)swDocumentTypes_e.swDocPART)PartURDFExporter();else AssemblyURDFExporter();}
        public void OpenLocalExport(){ShowTools(new MuJoCoToolsForm(MuJoCoSettings.DefaultPath,null,MuJoCoToolMode.Local));}
        public void OpenExistingPreview(){ShowTools(new MuJoCoToolsForm(MuJoCoSettings.DefaultPath,null,MuJoCoToolMode.Preview));}
        public void OpenProjectExport(){
            ModelDoc2 model=SwApp.ActiveDoc;if(model==null)return;string configuration=model.ConfigurationManager.ActiveConfiguration.Name;
            var form=new MuJoCoToolsForm(MuJoCoSettings.DefaultPath,null,MuJoCoToolMode.Project,()=>new ProjectExport((SldWorks)SwApp,model,configuration));
            form.SetProjectName(System.IO.Path.GetFileNameWithoutExtension(model.GetTitle()));ShowTools(form);
        }
        void ShowTools(MuJoCoToolsForm form){if(muJoCoTools!=null&&!muJoCoTools.IsDisposed){muJoCoTools.Close();if(!muJoCoTools.IsDisposed){form.Dispose();return;}}muJoCoTools=form;form.Show();form.BringToFront();}
        private JointPropertyManager jointPage;
        public void OpenJointConfiguration(){
            try{ModelDoc2 model=SwApp.ActiveDoc;bool error;var root=ConfigurationSerialization.LoadBaseNodeFromModel(model,out error);if(error||root==null)throw new InvalidOperationException("请先配置并保存 URDF 树。");
                CommonSwOperations.LoadSWComponents(model,root,new System.Collections.Generic.List<string>());var helper=new ExportHelper((SldWorks)SwApp);helper.GetSimulation().SetCollisionTree(root);
                jointPage=new JointPropertyManager(helper.GetSimulation(),JointDescriptor.FromTree(root));jointPage.Show();
            }catch(Exception e){MessageBox.Show(e.Message,"SWSimTool 关节配置");}
        }
        private SimulationPropertyManager simulationPage;
        public void OpenSimulationConfiguration(){
            try{ModelDoc2 model=SwApp.ActiveDoc;bool error;var root=ConfigurationSerialization.LoadBaseNodeFromModel(model,out error);if(error||root==null)throw new InvalidOperationException("请先配置并保存 URDF 树。");
                CommonSwOperations.LoadSWComponents(model,root,new System.Collections.Generic.List<string>());var helper=new ExportHelper((SldWorks)SwApp);helper.GetSimulation().SetCollisionTree(root);
                simulationPage=new SimulationPropertyManager(helper.GetSimulation(),root.Link.Name,JointDescriptor.FromTree(root).Where(d=>new[]{"revolute","continuous","prismatic"}.Contains(d.type)).ToDictionary(d=>d.name,d=>d.child));simulationPage.Show();
            }catch(Exception e){MessageBox.Show(e.Message,"SWSimTool 仿真配置");}
        }
        private SimulationPropertyManager constraintPage;
        public void OpenConstraintConfiguration(){
            try{ModelDoc2 model=SwApp.ActiveDoc;bool error;var root=ConfigurationSerialization.LoadBaseNodeFromModel(model,out error);if(error||root==null)throw new InvalidOperationException("请先配置并保存 URDF 树。");CommonSwOperations.LoadSWComponents(model,root,new System.Collections.Generic.List<string>());var helper=new ExportHelper((SldWorks)SwApp);helper.GetSimulation().SetCollisionTree(root);constraintPage=new SimulationPropertyManager(helper.GetSimulation(),root.Link.Name,JointDescriptor.FromTree(root).Where(d=>new[]{"revolute","continuous","prismatic"}.Contains(d.type)).ToDictionary(d=>d.name,d=>d.child),true);constraintPage.Show();}catch(Exception e){MessageBox.Show(e.Message,"SWSimTool 约束配置");}
        }
        public void OpenMuJoCoTools()
        {
            if (muJoCoTools == null || muJoCoTools.IsDisposed) muJoCoTools = new MuJoCoToolsForm();
            muJoCoTools.Show();
            if (muJoCoTools.WindowState == FormWindowState.Minimized) muJoCoTools.WindowState = FormWindowState.Normal;
            muJoCoTools.BringToFront();
        }

        public int ToolbarEnableMethod()
        {
            return 1;
        }
        private SWSimTool.UI.CollisionPropertyManager collisionPage;
        public int CollisionEnableMethod()
        {
            ModelDoc2 model=SwApp.ActiveDoc;
            return model!=null && model.GetType()==(int)swDocumentTypes_e.swDocASSEMBLY ? 1 : 0;
        }
        public void OpenCollisionConfiguration()
        {
            try
            {
                ModelDoc2 model=SwApp.ActiveDoc;
                if(model==null)throw new InvalidOperationException("请打开已配置 URDF link 树的装配。");
                bool error;
                var root=ConfigurationSerialization.LoadBaseNodeFromModel(model,out error);
                if(error||root==null)throw new InvalidOperationException("请先使用 URDF 配置并保存 link 树。");
                CommonSwOperations.LoadSWComponents(model,root,new System.Collections.Generic.List<string>());
                var helper=new ExportHelper((SldWorks)SwApp);
                helper.GetSimulation().SetCollisionTree(root);
                collisionPage=new SWSimTool.UI.CollisionPropertyManager(helper.GetSimulation(),root.Link.Name);
                collisionPage.Show();
            }
            catch(Exception e){MessageBox.Show(e.Message,"碰撞配置",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        public void RemoveCommandMgr()
        {
            CmdMgr.RemoveCommandGroup(MuJoCoCommandGroupId);
            for(int doc=0;doc<=3;doc++)SwApp.RemoveMenu(doc,"导出与预览@SWSimTool","");
            SwApp.RemoveMenu((int)swDocumentTypes_e.swDocASSEMBLY, "Export as URDF@&Tools", "");
            logger.Info("Removing assembly export from file menu");
            SwApp.RemoveMenu((int)swDocumentTypes_e.swDocPART, "Export as URDF@&Tools", "");
            logger.Info("Removing part export from file menu");
        }

        #endregion UI Methods

        #region UI Callbacks

        public void SetupAssemblyExporter()
        {
            ModelDoc2 modeldoc = SwApp.ActiveDoc;
            logger.Info("Assembly export called for file " + modeldoc.GetTitle());
            bool saveAndRebuild = false;
            if (modeldoc.GetSaveFlag())
            {
                saveAndRebuild = true;
                logger.Info("Save is required");
            }
            else if (modeldoc.Extension.NeedsRebuild2 !=
                (int)swModelRebuildStatus_e.swModelRebuildStatus_FullyRebuilt)
            {
                saveAndRebuild = true;
                logger.Info("A rebuild is required");
            }
            if (saveAndRebuild ||
                MessageBox.Show("The SW to URDF exporter requires saving and/or rebuilding before continuing",
                "Save and rebuild document?", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                int options = (int)swSaveAsOptions_e.swSaveAsOptions_SaveReferenced |
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
                logger.Info("Saving assembly");
                modeldoc.Save3(options, 0, 0);

                logger.Info("Opening property manager");
                SetupPropertyManager();
            }
        }

        public void AssemblyURDFExporter()
        {
            try
            {
                SetupAssemblyExporter();
            }
            catch (Exception e)
            {
                logger.Error("An exception was caught when trying to setup the assembly exporter", e);
                MessageBox.Show("There was a problem setting up the property manager: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        public void SetupPropertyManager()
        {
            ExportPropertyManager pm = new ExportPropertyManager((SldWorks)SwApp);
            logger.Info("Loading config tree");
            bool success = pm.LoadConfigTree();

            if (success)
            {
                logger.Info("Showing property manager");
                pm.Show();
            }
        }

        public void SetupPartExporter()
        {
            logger.Info("Part export called");
            ModelDoc2 modeldoc = SwApp.ActiveDoc;
            if ((modeldoc.Extension.NeedsRebuild2 == 0) ||
                MessageBox.Show("Save and rebuild document?",
                "The SW to URDF exporter requires saving before continuing",
                MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                if (modeldoc.Extension.NeedsRebuild2 != 0)
                {
                    int options = (int)swSaveAsOptions_e.swSaveAsOptions_SaveReferenced |
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
                    logger.Info("Saving part");
                    modeldoc.Save3(options, 0, 0);
                }

                PartExportForm exportForm = new PartExportForm((SldWorks)SwApp);
                logger.Info("Showing part");
                // Show is modeless. Do not Dispose here: that immediately destroys the window.
                // A modeless Form disposes itself when the user closes it.
                exportForm.Show();
                logger.Info($"PartExportForm Show returned: Visible={exportForm.Visible}, IsDisposed={exportForm.IsDisposed}");
            }
        }

        public void PartURDFExporter()
        {
            try
            {
                SetupPartExporter();
            }
            catch (Exception e)
            {
                logger.Error("Excoption caught setting up export form", e);
                MessageBox.Show("An exception occured setting up the export form, please email " +
                    " your maintainer with the log file found at " + Logger.GetFileName());
            }
        }

        public void FlyoutCallback()
        {
            FlyoutGroup flyGroup = CmdMgr.GetFlyoutGroup(flyoutGroupID);
            flyGroup.RemoveAllCommandItems();

            flyGroup.AddCommandItem(
                DateTime.Now.ToLongTimeString(), "test", 0, "FlyoutCommandItem1", "FlyoutEnableCommandItem1");
        }

        public int FlyoutEnable()
        {
            return 1;
        }

        public void FlyoutCommandItem1()
        {
            SwApp.SendMsgToUser("Flyout command 1");
        }

        public int FlyoutEnableCommandItem1()
        {
            return 1;
        }

        #endregion UI Callbacks

        #region Event Methods

        public bool AttachEventHandlers()
        {
            AttachSwEvents();
            //Listen for events on all currently open docs
            AttachEventsToAllDocuments();
            return true;
        }

        private bool AttachSwEvents()
        {
            try
            {
                SwEventPtr.ActiveDocChangeNotify +=
                    new DSldWorksEvents_ActiveDocChangeNotifyEventHandler(OnDocChange);
                SwEventPtr.DocumentLoadNotify2 +=
                    new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocLoad);
                SwEventPtr.FileNewNotify2 +=
                    new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNew);
                SwEventPtr.ActiveModelDocChangeNotify +=
                    new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnModelChange);
                SwEventPtr.FileOpenPostNotify +=
                    new DSldWorksEvents_FileOpenPostNotifyEventHandler(FileOpenPostNotify);
                return true;
            }
            catch (Exception e)
            {
                logger.Error("Attaching SW events failed", e);
                return false;
            }
        }

        private bool DetachSwEvents()
        {
            try
            {
                SwEventPtr.ActiveDocChangeNotify -=
                    new DSldWorksEvents_ActiveDocChangeNotifyEventHandler(OnDocChange);
                SwEventPtr.DocumentLoadNotify2 -=
                    new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocLoad);
                SwEventPtr.FileNewNotify2 -=
                    new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNew);
                SwEventPtr.ActiveModelDocChangeNotify -=
                    new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnModelChange);
                SwEventPtr.FileOpenPostNotify -=
                    new DSldWorksEvents_FileOpenPostNotifyEventHandler(FileOpenPostNotify);
                return true;
            }
            catch (Exception e)
            {
                logger.Error("Attaching SW events failed", e);
                return false;
            }
        }

        public void AttachEventsToAllDocuments()
        {
            ModelDoc2 modDoc = (ModelDoc2)SwApp.GetFirstDocument();
            while (modDoc != null)
            {
                if (!OpenDocs.Contains(modDoc))
                {
                    AttachModelDocEventHandler(modDoc);
                }
                else if (OpenDocs.Contains(modDoc))
                {
                    DocumentEventHandler docHandler = (DocumentEventHandler)OpenDocs[modDoc];
                    if (docHandler != null)
                    {
                        bool connected = docHandler.ConnectModelViews();
                        if (!connected)
                        {
                            logger.Warn("Failed to connect to model views");
                        }
                    }
                }

                modDoc = (ModelDoc2)modDoc.GetNext();
            }
        }

        public bool AttachModelDocEventHandler(ModelDoc2 modDoc)
        {
            if (modDoc == null)
            {
                return false;
            }

            if (!OpenDocs.Contains(modDoc))
            {
                DocumentEventHandler docHandler;
                switch (modDoc.GetType())
                {
                    case (int)swDocumentTypes_e.swDocPART:
                        {
                            docHandler = new PartEventHandler(modDoc, this);
                            break;
                        }
                    case (int)swDocumentTypes_e.swDocASSEMBLY:
                        {
                            docHandler = new AssemblyEventHandler(modDoc, this);
                            break;
                        }
                    case (int)swDocumentTypes_e.swDocDRAWING:
                        {
                            docHandler = new DrawingEventHandler(modDoc, this);
                            break;
                        }
                    default:
                        {
                            return false; //Unsupported document type
                        }
                }
                docHandler.AttachEventHandlers();
                OpenDocs.Add(modDoc, docHandler);
            }
            return true;
        }

        public bool DetachModelEventHandler(ModelDoc2 modDoc)
        {
            OpenDocs.Remove(modDoc);
            return true;
        }

        public bool DetachEventHandlers()
        {
            DetachSwEvents();

            //Close events on all currently open docs
            DocumentEventHandler docHandler;
            int numKeys = OpenDocs.Count;
            object[] keys = new Object[numKeys];

            //Remove all document event handlers
            OpenDocs.Keys.CopyTo(keys, 0);
            foreach (ModelDoc2 key in keys)
            {
                docHandler = (DocumentEventHandler)OpenDocs[key];
                docHandler.DetachEventHandlers(); //This also removes the pair from the hash
                docHandler = null;
            }
            return true;
        }

        #endregion Event Methods

        #region Event Handlers

        //Events
        public int OnDocChange()
        {
            foreach(ModelDoc2 model in OpenDocs.Keys)if(!ReferenceEquals(model,SwApp.ActiveDoc))ConfigurationSession.Invalidate(model);
            return 0;
        }

        public int OnDocLoad(string docTitle, string docPath)
        {
            return 0;
        }

        private int FileOpenPostNotify(string FileName)
        {
            AttachEventsToAllDocuments();
            return 0;
        }

        public int OnFileNew(object newDoc, int docType, string templateName)
        {
            AttachEventsToAllDocuments();
            return 0;
        }

        public int OnModelChange()
        {
            return 0;
        }

        #endregion Event Handlers
    }
}
