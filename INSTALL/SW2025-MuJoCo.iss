; Build with Inno Setup 6.5+: ISCC.exe INSTALL\SW2025-MuJoCo.iss
#ifndef Payload
#define Payload AddBackslash(SourcePath) + "..\build\simulation-candidate"
#endif
#define BuildVersion GetFileVersion(Payload + "\SW2URDF.dll")

[Setup]
AppId={{E43E85A9-071D-430A-91B2-84B7AB923170}
AppName=SW2MuJoCo for SolidWorks 2025
AppVersion={#BuildVersion}
AppVerName=SW2MuJoCo for SolidWorks 2025 ({#BuildVersion})
AppPublisher=SW2URDF contributors
AppPublisherURL=https://github.com/ros/solidworks_urdf_exporter
VersionInfoVersion={#BuildVersion}
VersionInfoProductName=SW2MuJoCo for SolidWorks 2025
VersionInfoDescription=SW2MuJoCo SolidWorks 2025 x64 Setup
DefaultDirName={autopf}\SolidWorks Corp\SolidWorks\URDFExporter
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=SW2MuJoCo_SW2025_x64_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\SW2URDF.dll
LicenseFile=..\LICENSE
InfoBeforeFile=MuJoCo-Setup-Readme.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; The tested assembly also contains upstream test classes: retain their DLL dependencies.
Source: "{#Payload}\*.dll"; DestDir: "{app}"; Excludes: "SolidWorks.Interop.*.dll"; Flags: ignoreversion
Source: "{#Payload}\SW2URDF.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\SW2URDF.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\images\*.png"; DestDir: "{app}\images"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\SW2025_SW2URDF_FIX.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "MuJoCo-Setup-Readme.txt"; DestDir: "{app}"; Flags: ignoreversion

Source: "{#Payload}\mujoco_backend\simplify_stl.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion
Source: "{#Payload}\mujoco_backend\mesh_cache.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion
Source: "{#Payload}\mujoco_backend\incremental.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion
Source: "{#Payload}\mujoco_backend\solver.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion
Source: "{#Payload}\mujoco_backend\requirements.txt"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion

Source: "..\docs\SW2MuJoCo_使用说明.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\SW2MuJoCo_3.2_使用指南.html"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\SW2MuJoCo_3.2_使用指南.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\guide-images\*.jpg"; DestDir: "{app}\docs\guide-images"; Flags: ignoreversion

Source: "{#Payload}\mujoco_backend\native_support.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\mujoco_backend\convert.py"
Type: files; Name: "{app}\mujoco_backend\equalities.py"
Type: files; Name: "{app}\mujoco_backend\robot_model.py"
Type: files; Name: "{app}\mujoco_backend\site_forces.py"
Type: files; Name: "{app}\mujoco_backend\sites.py"
Type: files; Name: "{app}\mujoco_backend\joints.py"
Type: files; Name: "{app}\mujoco_backend\collision.py"

[Code]
const
  ClsidKey = 'SOFTWARE\Classes\CLSID\{65c9fc17-6a74-45a3-8f84-55185900275d}\InprocServer32';
  AddinKey = 'SOFTWARE\SolidWorks\Addins\{65c9fc17-6a74-45a3-8f84-55185900275d}';

function RegAsmPath: String;
begin
  Result := ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe');
end;

function SolidWorksRunning: Boolean;
var
  Locator, Services, Processes: Variant;
begin
  { Never force-close SolidWorks: the user may have unsaved models. }
  Result := True;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name=''SLDWORKS.exe''');
    Result := Processes.Count > 0;
  except
    Log('Could not check running SolidWorks processes: ' + GetExceptionMessage);
  end;
end;

function OwnsRegistration: Boolean;
var
  CodeBase, Expected: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, ClsidKey, 'CodeBase', CodeBase) then
  begin
    Expected := ExpandConstant('{app}\SW2URDF.dll');
    StringChangeEx(Expected, '\', '/', True);
    Result := CompareText(CodeBase, 'file:///' + Expected) = 0;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0) and FileExists(RegAsmPath);
  if not Result then
    SuppressibleMsgBox('Microsoft .NET Framework 4.8 or later is required. Install it, then run Setup again.', mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if SolidWorksRunning then
    Result := 'Save your models and close ALL SolidWorks windows before installing. Setup will not close them automatically. If they are already closed, check that Windows WMI is available, then retry.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if not Exec(RegAsmPath, '/codebase "' + ExpandConstant('{app}\SW2URDF.dll') + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('Unable to start 64-bit RegAsm: ' + SysErrorMessage(ExitCode));
    if ExitCode <> 0 then
      RaiseException('64-bit COM registration failed. RegAsm exit code: ' + IntToStr(ExitCode));
    { Upstream ComRegisterFunction catches errors internally, so verify its effects too. }
    if not OwnsRegistration or not RegKeyExists(HKLM64, AddinKey) then
      RaiseException('SW2URDF registration verification failed. Run Setup as administrator.');
    Log('Verified SW2URDF COM registration for ' + ExpandConstant('{app}\SW2URDF.dll'));
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := not SolidWorksRunning;
  if not Result then
    SuppressibleMsgBox('Save your models and close ALL SolidWorks windows before uninstalling.', mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
begin
  if (CurUninstallStep = usUninstall) and OwnsRegistration then
  begin
    if not Exec(RegAsmPath, '/unregister "' + ExpandConstant('{app}\SW2URDF.dll') + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('Unable to start 64-bit RegAsm: ' + SysErrorMessage(ExitCode));
    if ExitCode <> 0 then
      RaiseException('COM unregistration failed. RegAsm exit code: ' + IntToStr(ExitCode));
  end;
end;
