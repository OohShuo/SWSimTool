"""Static contract checks; no SolidWorks process or CAD document is opened."""
from pathlib import Path

root = Path(__file__).resolve().parents[2]
host = root / "src/SWSimTool.SolidWorks"
addin = (host / "SW/SwAddin.cs").read_text(encoding="utf-8-sig")
menu = addin.split("public void OpenUrdfExport(){", 1)[1].split("bool CanOpenExport", 1)[0]
assert "AssemblyExportPreparation.Prepare" in menu
assert "AssemblyURDFExporter()" not in menu
assert "PartURDFExporter()" in menu
page = (host / "URDFExport/ExportPropertyManager.cs").read_text(encoding="utf-8-sig")
button = page.split("private void ExportButtonPress()", 1)[1].split("private void EnableControl", 1)[0]
assert "AssemblyExportPreparation.Prepare" in button
assert "confirmedClose" in button
service = (host / "URDFExport/AssemblyExportPreparation.cs").read_text(encoding="utf-8-sig")
assert "PMCompute" not in service and "PropertyManager" not in service
assert "project?.urdf_export" in service and "tree.Clone()" in service
form = (host / "UI/AssemblyExportForm.cs").read_text(encoding="utf-8-sig")
assert "BaseNode = new LinkNode(node.Snapshot())" in form
assert "SaveConfigTree(" not in form, "Export/cancel must not persist the export working copy"
assert "jointLimits.Commit" in (host / "URDFExport/ExportPropertyManagerExtension.cs").read_text(encoding="utf-8-sig")
print("PASS: direct menu and original button share control-free preparation; export/cancel cannot persist drafts")
