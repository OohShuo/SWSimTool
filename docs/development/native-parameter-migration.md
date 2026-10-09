# Native left-side parameter migration

## Stage 1: independent joint configuration page

JointPropertyManager now creates a native PropertyManager group and delegates
input callbacks to NativeParameterFields/NativeJointEditor. The production page
no longer creates a WindowFromHandle or sets an editor HWND/host height.

The native bindings are page owned. PageDraft still provides the independent
business working copy. Native COM callbacks only route input; validation and
transaction persistence remain outside the callback handler. No RobotModel,
exporter, backend, physical defaults or comparison tolerances were changed.

Controls are allocated once. Joint selection reloads field values; type and
solver toggles change visibility/captions without recreating controls. Invalid
raw input blocks joint switching and save, retaining input for correction.
Hidden solver edits survive disabling and enabling overrides. Field writes
suppress synchronous native callback reentry. Cancel/disposal discards pending
page edits; invalidated sessions reject native callbacks and saves.

Legacy JointEditorControl is not instantiated by the production joint page.
It remains for its existing regression fixtures and the scroll helper shared
by pages that have not yet migrated. Those pages are not declared migrated.

Verification on 2026-10-09:
- PASS: Release plugin build and source workspace cleanliness.
- PASS: Test-NativeJointPage.ps1: native control types/unique IDs; driven and
  undriven defaults; conditionals; solver input retention; entity switching;
  invalid raw input; no control recreation; callback reentry; complete draft
  collection; failed save/retry; cancel; reopen; stale callback rejection.
- PASS: Test-ConfigurationRebuild.ps1: existing configuration lifecycle,
  transaction failure, stable identities and persistence regressions.
- PASS: git diff --check.
- NOT RUN: real SolidWorks joint page, narrow sidebar, 100%/125%/150% DPI,
  native scrolling/fold/unfold, CAD preview/export and installation.

The user's earlier GUI verification concerned the URDF joint limit fields,
not this independent joint configuration page. Do not reuse that result here.

Remaining stages: collision parameter region, simulation parameter regions,
and constraints. Migrate and commit each page independently; preserve custom
complex trees and previews. Native PropertyManager is not a WinForms flow layout:
creation order, alignment, grouping and visibility remain explicit.