# Dropdown wheel selection protection

Behavior: hovering or focusing a plugin dropdown must not change its selected
value through vertical or horizontal wheel input. Click/key selection and the
original editable/read-only mode remain available.

WinForms: NoWheelComboBox consumes wheel messages and forwards vertical wheel
motion to an existing scrollable parent. CollisionComboBox retains its existing
DropDownList defaults and now inherits that shared wheel behavior. Export and
settings forms use NoWheelComboBox without changing their configured styles.

Native PropertyManager: DropdownWheelGuard observes removed messages on the
current UI thread with WH_GETMESSAGE. It does not install a global hook or cancel
messages destined for other SW controls. NativeParameterFields restores the
model-derived selection instead of invoking its write callback for wheel input.
The URDF page similarly remembers/restores its own registered dropdowns. Each
page owns the observer lifetime, retaining it for failed-save retries and
releasing it on final close/show failure. The delegate remains rooted while its
hook exists. Reference-axis and coordinate-system controls previously shared ID
19; axes now use the distinct ID 113 so rollback addresses the correct control.

Implementation reference:
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw

Verification on 2026-10-09:
- PASS: Release plugin build and workspace cleanliness.
- PASS: Test-DropdownWheel.ps1: vertical/horizontal wheel messages; actual local
  Windows message queue observer; key input clearing wheel context; unchanged
  editable mode; native selection rollback; no draft write on wheel; ordinary
  choice writes; registered-control scope; nested lifetime and observer removal.
- PASS: Test-NativeJointPage.ps1 and Test-UrdfExportRetry.ps1.
- PASS: Test-CollisionNavigation.ps1, including existing config and cache cases.
- PASS: Test-UrdfEntryPoints.py and git diff --check.
- NOT RUN: real SW hover/focus/open-popup behavior and installation.

No physical parameters, persistence format or input validation were changed.