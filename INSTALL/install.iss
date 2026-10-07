; Build with Inno Setup 6.5+: ISCC.exe INSTALL\install.iss
#ifndef Payload
#define Payload AddBackslash(SourcePath) + "..\build\runtime-release"
#endif
#define BuildVersion GetFileVersion(Payload + "\SWSimTool.dll")
#ifndef ReleaseName
#define ReleaseName "3.2.1"
#endif

[Setup]
AppId={{6506C8C8-4D78-4A53-91A4-EF24B72BFA25}
AppName=SWSimTool for SolidWorks 2025
AppVersion={#BuildVersion}
AppVerName=SWSimTool for SolidWorks 2025 ({#BuildVersion})
AppPublisher=SWSimTool contributors
AppPublisherURL=https://github.com/OohShuo/sw2mujoco
VersionInfoVersion={#BuildVersion}
VersionInfoProductName=SWSimTool for SolidWorks 2025
VersionInfoDescription=SWSimTool SolidWorks 2025 x64 Setup
DefaultDirName={autopf}\SWSimTool
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\build\dist
OutputBaseFilename=SWSimTool_{#ReleaseName}_SW2025_x64_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\SWSimTool.dll
LicenseFile={#Payload}\LICENSE
InfoBeforeFile={#Payload}\docs\INSTALL.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#Payload}\SWSimTool.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\SWSimTool.Application.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\SWSimTool.Infrastructure.dll"; DestDir: "{app}"; Flags: ignoreversion
; Explicit production payload: tests and reference generators are never installed.
Source: "{#Payload}\SWSimTool.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\CsvHelper.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\MathNet.Numerics.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\log4net.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\solidworkstools.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\SWSimTool.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\SWSimTool.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\images\*.png"; DestDir: "{app}\images"; Flags: ignoreversion
Source: "{#Payload}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\docs\INSTALL.md"; DestDir: "{app}\docs"; Flags: ignoreversion

Source: "{#Payload}\mujoco_backend\simplify_stl.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion
Source: "{#Payload}\mujoco_backend\mesh_cache.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion
Source: "{#Payload}\mujoco_backend\incremental.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion
Source: "{#Payload}\mujoco_backend\solver.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion
Source: "{#Payload}\mujoco_backend\requirements.txt"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion

Source: "{#Payload}\docs\SWSimTool_3.2.1_使用指南.html"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#Payload}\docs\SWSimTool_3.2.1_使用指南.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#Payload}\docs\guide-images\*.jpg"; DestDir: "{app}\docs\guide-images"; Flags: ignoreversion

Source: "{#Payload}\mujoco_backend\native_support.py"; DestDir: "{app}\mujoco_backend"; Flags: ignoreversion

[Code]
const
  ClsidKey = 'SOFTWARE\Classes\CLSID\{974a302b-3966-4e45-a5a5-1c24c26b7faa}\InprocServer32';
  AddinKey = 'SOFTWARE\SolidWorks\Addins\{974a302b-3966-4e45-a5a5-1c24c26b7faa}';

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
    Expected := ExpandConstant('{app}\SWSimTool.dll');
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
    if not Exec(RegAsmPath, '/codebase "' + ExpandConstant('{app}\SWSimTool.dll') + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('Unable to start 64-bit RegAsm: ' + SysErrorMessage(ExitCode));
    if ExitCode <> 0 then
      RaiseException('64-bit COM registration failed. RegAsm exit code: ' + IntToStr(ExitCode));
    { Upstream ComRegisterFunction catches errors internally, so verify its effects too. }
    if not OwnsRegistration or not RegKeyExists(HKLM64, AddinKey) then
      RaiseException('SWSimTool registration verification failed. Run Setup as administrator.');
    Log('Verified SWSimTool COM registration for ' + ExpandConstant('{app}\SWSimTool.dll'));
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
    if not Exec(RegAsmPath, '/unregister "' + ExpandConstant('{app}\SWSimTool.dll') + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('Unable to start 64-bit RegAsm: ' + SysErrorMessage(ExitCode));
    if ExitCode <> 0 then
      RaiseException('COM unregistration failed. RegAsm exit code: ' + IntToStr(ExitCode));
  end;
end;
