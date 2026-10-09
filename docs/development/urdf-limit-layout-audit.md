# Native URDF joint parameter fields

The old WinForms WindowFromHandle parameter host did not reserve enough space
in the real PropertyManager layout. The component selector overlapped its last
rows. Control-only size tests could not certify native placement.

The region now uses four native labels and four native textboxes. SolidWorks
owns their layout. There is no parameter HWND, manual host height, height event,
or pixel/dialog-unit conversion. The unrelated URDF tree window is unchanged.

UrdfJointLimitFields updates visibility and SI captions without recreating
controls. Revolute/prismatic show four fields; continuous hides bounds;
fixed/root hide all fields. Type changes retain hidden input values. Loading
uses Limit.GetInputTexts and committing retains atomic Limit.SetInputs validation.
No physics or persistence format is changed.

Verification on 2026-10-09:
- PASS: Release plugin build and workspace cleanliness gate.
- PASS: Test-UrdfLimitLayout.ps1: native types, unique IDs, type/root visibility,
  units, hidden edited input retention, optional blanks and atomic validation.
  This replaces the embedded-control test, retaining relevant input coverage.
- PASS: Test-ConfigurationRebuild.ps1: lifecycle and parameter persistence.
- PASS: Test-UrdfExportRetry.ps1: retry, Previous and joint switching.
- PASS: git diff --check.
- NOT RUN: real SW GUI/DPI, CAD export and installation.
  Mock tests do not certify actual GUI placement.

User verification (2026-10-09): the user reports this round's changes have been
verified in their environment. This is user-reported verification, not an agent
GUI run. Specific sidebar widths and 100%/125%/150% scaling coverage were not
reported; those remain explicit acceptance cases for subsequent page migrations.
