# Configuration lifecycle repair — pre-fix evidence

Baseline: `5d390fd`, Windows / SolidWorks 2025 / .NET 4.8 and 8. Installed and built SWSimTool.dll are identical: SHA256 `c94628ea7767ab8119f6dddca9c1c50d3aa3e7490681bcacb78d862d6d4cfb0ba`, version 3.2.1, product revision 9e5bcb2. The supplied ProjectExport → BuildNative → ResolveNativeGeometry stack matches this source. A loaded-process path could not be recorded: SolidWorks was not running at audit time.

`Test-ConfigurationRebuild.ps1 -ExpectKnownFailure` reproduces two independent defects against this baseline:

* An AttachmentService opened before node deletion retains its SimulationProject. After an absent-node load and replacement tree save, calling the old service Save writes the old site/link ID into the new envelope. The replacement tree has a different link ID. This proves a stale draft contamination path, not that every reported user occurrence used this path. The API has no session/version precondition. SaveConfigTree also writes its previously loaded simulation draft after SaveTree.
* Two distinct tree nodes with distinct IDs but the same joint name cause JointDescriptor.FromTree(...).ToDictionary(name) to throw the generic duplicate-key exception. This proves an independent real-name collision path; the historical message without stack does not establish whether the user's duplicate was this path or a repeated ID/traversal.

No name fallback, dictionary overwrite or removal of configuration was used to obtain these reproductions. The test models use Moq CAD interfaces, not user CAD files. Real CAD deletion/rebuild, save/reopen and GUI flows remain NOT RUN at this checkpoint; they must be exercised with owned fixtures before final acceptance. Baseline output and version records are in build/reports/config-lifecycle and build/logs/lifecycle-baseline.log. First test launch failed on sandbox TEMP permissions; rerun uses build/test-work.

The two previously untracked guide read helpers are preserved and excluded from these commits. Earlier reverted implementation is not restored.
