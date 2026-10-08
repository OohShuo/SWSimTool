param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $root ('build/'+$Payload)
$refs=@("$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/SWSimTool.Core.dll",'System.Core','System.Web.Extensions')
$refs|Where-Object {$_ -like '*.dll'}|ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using SWSimTool.Simulation;using SWSimTool.Persistence;
public static class ReplacementTest {
 sealed class Store:IConfigurationTransactionStore {public string Data="old";public int Writes;public bool Fail,Corrupt,RollbackFail;public string ReadConfiguration(){return Data;}public void WriteConfiguration(string data){Writes++;Data=data;if(Fail)throw new IOException("injected write");if(Corrupt)Data="corrupt";}public void RestoreConfiguration(string data){if(RollbackFail)throw new IOException("injected rollback");Data=data;}}
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 public static void Run(string folder){
  var s=new Store();bool rejected=false;try{ConfigurationReplacement.Replace(s,"new",()=>{},x=>{},x=>{throw new IOException("backup unavailable");});}catch(IOException){rejected=true;}Assert(rejected&&s.Writes==0&&s.Data=="old","Backup failure wrote document");
  s=new Store();rejected=false;try{ConfigurationReplacement.Replace(s,"new",()=>{},x=>{throw new InvalidDataException("invalid draft");},x=>"unused");}catch(InvalidDataException){rejected=true;}Assert(rejected&&s.Writes==0,"Invalid draft wrote document");
  foreach(bool corrupt in new[]{false,true}){s=new Store{Fail=!corrupt,Corrupt=corrupt};rejected=false;try{ConfigurationReplacement.Replace(s,"new",()=>{},x=>{},x=>ConfigurationBackup.Write(folder,"测试.SLDASM","默认",x,"test"));}catch(IOException){rejected=true;}Assert(rejected&&s.Data=="old","Write/readback failure did not roll back");}
  s=new Store{Fail=true,RollbackFail=true};rejected=false;try{ConfigurationReplacement.Replace(s,"new",()=>{},x=>{},x=>"backup");}catch(AggregateException e){rejected=e.InnerExceptions.Count==2&&e.Message.Contains("backup");}Assert(rejected,"Rollback failure not distinguished");
  s=new Store();int checks=0;rejected=false;try{ConfigurationReplacement.Replace(s,"new",()=>{if(++checks==2)throw new InvalidDataException("expired lease");},x=>{},x=>"backup");}catch(InvalidDataException){rejected=true;}Assert(rejected&&s.Writes==0,"Expired session wrote replacement");
  s=new Store();var path=ConfigurationReplacement.Replace(s,"new",()=>{},x=>{},x=>ConfigurationBackup.Write(folder,"测试.SLDASM","默认",x,"test"));Assert(s.Data=="new"&&ConfigurationBackup.Read(path).payload=="old","Success or recoverable backup mismatch");
  var data=File.ReadAllText(path);File.WriteAllText(path,data.Replace("old","tampered"));rejected=false;try{ConfigurationBackup.Read(path);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Tampered backup accepted");
  string raw="{\"version\":2,\"extra\":\"keep\",\"configurations\":{\"Default\":{\"configuration_id\":\"swcfg:7\",\"simulation\":{\"oldDanglingID\":\"A\"}},\"Other\":{\"configuration_id\":\"swcfg:8\",\"unknown\":{\"value\":9}}}}";
  string fresh="{\"configuration_id\":\"swcfg:7\",\"simulation\":{\"unsaved\":42}}";
  string replaced=ConfigurationEnvelopeReplacement.ReplaceEntry(raw,"Default","swcfg:7",fresh);
  Assert(replaced.Contains("unsaved")&&!replaced.Contains("oldDanglingID")&&replaced.Contains("unknown")&&replaced.Contains("keep"),"Replacement merged old target or discarded other entries");
  replaced=ConfigurationEnvelopeReplacement.ReplaceEntry(raw,"Renamed","swcfg:7",fresh);Assert(replaced.Contains("Renamed")&&!replaced.Contains("Default")&&replaced.Contains("Other"),"Configuration identity rename mismatch");
  rejected=false;try{ConfigurationEnvelopeReplacement.ReplaceEntry(raw,"Default","swcfg:new",fresh);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Same-name replacement inherited another SW configuration identity");
  Console.WriteLine("PASS: backup/validation/session failures prevent writes; write/readback roll back; rollback failures preserve evidence; Unicode backup round-trip and tamper detection");
 }
}
'@
[ReplacementTest]::Run((Join-Path $root 'build/test-work/配置备份'))
