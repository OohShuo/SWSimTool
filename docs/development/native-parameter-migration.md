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

Subsequent collision, simulation and constraint implementation stages are
recorded below. Each page retains custom complex trees and previews. Native PropertyManager is not a WinForms flow layout:
creation order, alignment, grouping and visibility remain explicit.
## Stage 2: collision presentation

CollisionPropertyManager now uses native tabs, groups, text/choice/check inputs,
lists and buttons. Its WindowFromHandle and fixed host height are removed.
NativeEditorPresenter is a presentation adapter over the existing page-local
input controller. The controller still uses unattached WinForms controls as
input bindings; they are not shown or assigned to a native HWND. This is an
incremental presentation migration, not a rewrite of the domain/persistence layer.

Controls are allocated before Show2. An empty collision pair has a hidden input
template so selecting/adding a pair reuses those native slots. Field templates
are keyed by layout path and control type; obsolete source references are
replaced on synchronization. A missing template raises an explicit diagnostic,
never adds controls to a displayed page or writes partial configuration.

PASS: Release build; native presentation callbacks, reentry, hidden field
retention, tab routing, stale-page rejection and disposal tests; collision
navigation, references and persistence regression with the native presenter.
NOT RUN: real SolidWorks tabs/layout, scroll position and DPI acceptance.

## Stage 3: simulation presentation

The simulation route now uses native PropertyManager tabs for attachments,
sensors, actuators, global solver settings and two-site force/spring settings.
The upper owner selector and footer actions remain independent groups; the
force tab hides the owner group as before. Native tab callbacks synchronize
only the page-local selected section and cancel an armed CAD picker.

Empty rule categories prepare detached, hidden field templates before Show2;
the placeholder objects are never inserted into the business draft. Real rule
selection/new rules bind to the same native controls. This preserves complete
unsaved draft collection and avoids AddControl2 while a page is displayed.
Existing input controllers remain page local and unattached, as in stage 2.

PASS: build, native presentation test, synchronized collision/simulation/
constraint controller regressions including conditional kp/kv/fovy, disabled
solver settings, references, spring input retention, complete draft collection,
persistence failure/retry and actual timestep-only page save classification.
NOT RUN: actual SolidWorks visual layout, tabs, CAD and DPI acceptance.

## Stage 4: constraints and final code audit

The constraints-only production route uses the same native presentation seam;
it no longer creates a WindowFromHandle. Its native tab contains connect/weld/
joint type-dependent inputs, existing site/joint choices and local solver inputs.
No owner selector or CAD picker is added to constraints. Legacy body-to-site
conversion remains explicit. URDF tree display remains its own custom region;
its parameters and limit inputs were already native.

All parameter surfaces now use native display controls. Existing collision and
simulation WinForms input controllers are deliberately retained as page-local,
non-displayed adapters, not shared drafts or embedded display windows. Removing
that adapter layer is outside this presentation-only change. No physical rules,
Stable ID policy, schema or solver comparison tolerances changed.

Verification: PASS Release build; native presentation callbacks/slots; synchronized
page controller regressions; native source-surface audit. The audit confirms all
three independent production PropertyManagers have no embedded editor HWND,
route input and native tab callbacks, and install/dispose wheel guards. Control
allocation is sealed before Show2; a missing template produces a diagnostic.

NOT RUN: real SolidWorks narrow-side-panel layout, 100%/125%/150% DPI, native
scrolling to the bottom, fold/unfold and repeat-open visual acceptance; real CAD
export/preview and installer/install acceptance. Legacy WinForms scroll checks
remain regression checks for the adapter only and do not prove native scrolling.
No SolidWorks process or user CAD was opened; no installer was updated.
