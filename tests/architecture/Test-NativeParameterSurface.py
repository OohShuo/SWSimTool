"""Native PropertyManager presentation contracts; no SolidWorks is opened."""
from pathlib import Path
root = Path(__file__).resolve().parents[2]
ui = root / "src/SWSimTool.SolidWorks/UI"
for name in ("CollisionPropertyManager.cs", "SimulationPropertyManager.cs", "JointPropertyManager.cs"):
    source = (ui / name).read_text(encoding="utf-8-sig")
    assert "swControlType_WindowFromHandle" not in source, name
    assert "SetWindowHandle" not in source and "window.Height" not in source, name
    assert "DropdownWheelGuard" not in source and "wheelGuard" not in source, name
for name in ("CollisionPropertyManager.cs", "SimulationPropertyManager.cs"):
    source = (ui / name).read_text(encoding="utf-8-sig")
    assert "page.AddTab(100+tab" in source and "presenter.OnTab(Id)" in source
    for event in ("OnText", "OnChoice", "OnList", "OnButton", "OnCheck"):
        assert "presenter." + event in source, (name, event)
    assert "service.RequireCurrentDocument" in source
    assert "presenter?.Dispose()" in source or "presenter.Dispose()" in source
presenter = (ui / "NativeEditorPresenter.cs").read_text(encoding="utf-8-sig")
assert "sealedControls=true" in presenter and "if(sealedControls)throw" in presenter
assert "DropdownWheelGuard" not in presenter and "IsWheelInput" not in presenter
assert "timer.Stop();timer.Dispose()" in presenter
assert "Save(" not in presenter and "SimulationStorage" not in presenter
urdf = (root / "src/SWSimTool.SolidWorks/URDFExport/ExportPropertyManager.cs").read_text(encoding="utf-8-sig")
assert "new UrdfJointLimitFields" in urdf
print("PASS: native parameters/tabs, no embedded editor HWND, standard selection callbacks/lifecycle guards, no persistence in presentation adapter")
