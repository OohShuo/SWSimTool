# Configuration draft / UI ownership repair

Baseline: `8b01c00796d058740ee757f59088c77487054568`.
Implementation: `codex/configuration-lifecycle`, in the isolated checkout under
`build/worktrees/configuration-lifecycle`. The original checkout and user CAD
projects are not modified. No SolidWorks session is started for this work.

## Confirmed causes

The first lifecycle implementation stored a `LinkNode` in the shared editing
context and attached that object directly to each PropertyManager TreeView.
`LinkNode` inherits `TreeNode`, so the business draft acquired control ownership.
Reopening a page could therefore try to attach an already-owned root.

The baseline also independently cloned the display children and the business
children, and `RebuildLink`, validation and serialization changed their input.
Cancellation still called `SaveActiveNode`. Dragging removed a node before
checking whether the target was a descendant, without restoring it on failure.
These defects made fixing only the repeated attachment insufficient.

## Implemented boundaries

- `ConfigurationEditingContext` holds a business `Link` tree and simulation
  configuration. It holds no TreeNode, TreeView, page or page callbacks. Business
  copies retain persistent CAD references and remove live component COM objects.
- `UI/ConfigurationPageDraft` owns one independent working copy per document.
  Opening another editor is refused until the active editor is confirmed or
  cancelled. No multi-page merge algorithm is introduced.
- Each URDF page constructs its own display nodes. Snapshot creation establishes
  one parent/child business tree without changing the display tree's data.
- `LinkNode.Clone` builds nodes from that snapshot, so its `Link.Children` and
  child node `Link` objects agree. Property copies preserve IDs, PID arrays and
  mesh quality flags. Parent/root state is derived from the display topology.
- Validation, descriptor reads, fingerprint serialization and tree serialization
  use independent candidate copies. Legacy identity materialization is explicit
  during candidate normalization; reading does not materialize input identities.
- Editor input changes affect the page copy. Cancel disposes it. Save collects
  inputs, validates an independent candidate, writes it, and only then updates
  the confirmed business draft. Failed writes preserve saved data, the confirmed
  draft and the page's retry inputs.
- Illegal root/self/descendant moves are rejected before removal. Valid moves
  synchronize parent links; insertion failure restores the original position.
- Initializer failures, close and cancel release the page lease, timers, preview,
  managed controls and callbacks. Closed PropertyManagers release their editor
  and page references. Undo/redo callbacks invalidate old sessions and derived
  caches, as do the existing deletion/configuration/close paths.
- The existing URDF preview/export path captures its independent input before
  closing the editor and does not export if confirmation/save fails.

## Rebuild behavior retained

Tools > SWSimTool > Rebuild Configuration collects the active page's inputs into
an independent candidate together with the complete confirmed draft. It does not
first commit that input to shared state. CAD resolution uses a detached calculation
service; it does not temporarily replace the shared project during validation.

The verified backup/write/readback/rollback transaction still replaces only the
current SW-configuration entry, preserves other entries, migrates the legacy
SW2MuJoCo node, retains valid IDs and CAD references, and never repairs an existing
dangling ID by matching its name. Success closes the old page, invalidates models,
tasks and caches, and obtains a new editing context. No reset/restore product
menus are introduced.

## Automated validation

The Windows build/test gate covers net48/net8.0, backend failure contracts,
native MJCF compilation/semantic/dynamics parity, stable references, CAD v2
persistence, raw-node migration rollback and typed atomic publication.

Targeted mock/WinForms checks cover:

- page isolation; a second page rejected without releasing the original owner;
- cancelled edits leave both the shared draft and persisted bytes unchanged;
- failed write retains page input and previous shared/persisted state, then retry;
- successful confirmation, storage invalidation and cold read preserve new IDs;
- deleted/replaced nodes, configuration switch, document close and the history
  callback prevent stale save/rebuild;
- coherent cloned children and parent pointers, independent editable properties;
- serialization/validation leave even an unmaterialized source identity intact;
- root/descendant drag rejection and successful reparenting;
- initializer failure releases ownership and permits retry;
- disposed page callbacks are rejected;
- existing conditional controls, scrolling and incremental mock source checks.

The independent backend regressions retain zero source preparation/simplification
and one MJCF generation/validation for cached input. Mock source tests retain zero
geometry queries/STL exports for solver-only changes. These results do not claim
real CAD performance acceptance. Physics tolerances and frozen fixtures are unchanged.

The test host additionally fixes its stdout/stderr encoding contract to UTF-8:
the Git Bash packaging failure was a UnicodeDecodeError while reading captured
console output, not a failed physics comparison. This change affects tests only.

## NOT RUN / remaining acceptance

Real SolidWorks GUI, COM event delivery, CAD-reference resolution during rebuild,
undo/redo interaction, save-assembly/close/reopen/export, legacy multi-configuration
GUI migration and real full-then-incremental export are NOT RUN under the current
code-only restriction. They require a later isolated CAD acceptance run. Installer
build/install/uninstall and installed DLL identity verification are NOT RUN.
Linux execution is NOT RUN on this Windows host. Automated mocks cannot replace
these checks, and no current installation is claimed to contain this repair.

Implementation commits:

- `3ab769f`: coherent business snapshots and read-only tree consumers.
- `1372303`: isolated editor copies, single-page ownership, confirm/cancel/retry,
  history invalidation, rebuild collection, resource release and targeted tests.
- `bed1be4`: explicit UTF-8 test-host output under Git Bash.

The complete pre-commit Windows gate exited 0 after the final code changes.
Detailed current automated outputs are in `build/logs/test-*.log`; compiled model
parity reports are in `build/native-parity-report.json` and
`build/reports/target-parity-net8.json`. Final commit references and candidate DLL
SHA256 are recorded after the post-commit regression.
