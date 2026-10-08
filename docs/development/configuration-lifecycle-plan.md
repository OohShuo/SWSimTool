# Configuration lifecycle repair from 8b01c0

Baseline: `8b01c00796d058740ee757f59088c77487054568` (2026-10-08).
Branch: `codex/configuration-lifecycle`. The original main checkout and its uncommitted changes are preserved. No later product entry points are part of this baseline.

Scope: code, builds, mock/static/automated regression only. No SolidWorks process, desktop automation, user CAD, installation, COM integration or GUI acceptance is authorized for this run. These checks must remain NOT RUN.

Confirmed source defects: AttachmentService retains a project without an instance/revision lease; SaveConfigTree writes the tree then writes the old project a second time; absent attributes do not invalidate all derived state; direct site/tree deletion omits dependency cleanup; Find accepts the first matching node; export publication does not verify the persisted configuration generation. Duplicate dictionary failures need their own reproduction and cannot be assumed to be the same failure as a dangling ID.

Implementation stages:
1. Baseline tests and mock reproductions.
2. Configuration leases, consistent legacy identity materialization, atomic complete tree/project saves and guarded export publication.
3. Dependency deletion, actionable reference diagnostics and editable unresolved drafts.
4. Shared document/configuration draft and menu-only rebuild, fresh CAD resolution and forced full next export.
5. Verified transactional rebuild/migration from legacy node, preservation of all other SW configurations, readback/deletion failure rollback.
6. Complete non-CAD regression and documentation; staged local commits.

Product requirements: only Tools > SWSimTool > Rebuild Configuration; no reset/restore menus or footer rebuild buttons. Right-click deletion removes the complete attribute (and all SW configuration entries it contains), invalidates drafts/tasks/caches and permits fresh construction. A stale draft cannot rebuild a replacement node. A legitimately loaded legacy draft can rebuild without an intermediate save/export. Valid IDs and CAD references are retained; no same-name fallback for a dangling ID. Removed draft objects never return from saved data. Automatic backups serve transaction rollback only.

Acceptance: complete unsaved draft across all collections; repaired current draft is not blocked by old target references; failures preserve raw old data and user draft; legacy node -> unsaved edits -> rebuild/new node -> mock reopen, ID and other-configuration retention; dangling IDs rejected on export; duplicate identity/name diagnostics; existing physics tolerances and frozen fixtures unchanged. Native timestep-only pure regression remains required; real CAD counts and GUI flows are NOT RUN.
`nBaseline results: Windows automated suite PASS (build.ps1 -Test, net48/net8.0 and Python/MuJoCo regressions). Mock reproduction PASS: deleted-node stale-page contamination and independent duplicate-joint-name failure reproduced. CAD/GUI/installation/Linux NOT RUN.
