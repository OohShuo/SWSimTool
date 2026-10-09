# Native input behavior: return to SolidWorks defaults

Follow-up to the native parameter migration (2026-10-09).

Removed DropdownWheelGuard and all production registrations, releases,
wheel-origin decisions and selection restores. Native click, keyboard and wheel
handling now belong to SolidWorks. Native callbacks still write the page-local
working copy and update dependent inputs. Existing WinForms NoWheelComboBox and
CollisionComboBox implementations were not changed.

NativeEditorPresenter now reloads selection only for a changed binding, option
list or business-side selection. Its polling pass no longer restores selection
merely because the native control currently differs from the previous working
copy value. The source binding survives unchanged polling; obsolete bindings
are cleared. Programmatic reloads retain the existing reentry guard.

URDF SelectComboBox uses a scoped loading flag to avoid treating programmatic
intermediate selections as user edits. NativeJointEditor retains explicit
initialization/object-switch loads and normal draft validation. No physics,
Stable ID or persistence semantics were changed.

PASS: Release build and workspace cleanliness.
PASS: Test-NativePresentation: pending second/third native choices survive
synchronization; callbacks update dependent visibility; repeated synchronization
preserves selection, while changed bindings load their current value.
PASS: Test-NativeJointPage: second/third type selection reaches draft, save and
reopen retain the third selection, and cancelled subsequent input is discarded.
PASS: Test-CollisionNavigation and Test-UrdfExportRetry.
PASS: Test-DropdownWheel for unchanged WinForms behavior only; native hook tests
were removed with the deleted implementation.
PASS: native surface/URDF entry-point source checks and git diff --check.

NOT RUN: actual SolidWorks click/keyboard/wheel selection, popup behavior, CAD
integration, installer build/install. No SolidWorks or user CAD was opened.
The earlier dropdown-wheel audit describes historical behavior superseded by
this change, not the current native-input policy.
