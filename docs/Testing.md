# Test tiers

Run `tools/build/RunSuite.ps1 -Tier Fast|Medium|SolidWorksIntegration|Release` from a Windows checkout. The default is Fast. Set `-Python` and `-SolidWorksDir` for local installations; the CAD runner also needs Inno Setup for Release.

| Tier | Environment | Gates |
|---|---|---|
| Fast | .NET SDK, net48 runtime, Python standard library | actual reusable projects; immutable snapshots, transforms/inertia, stable references, explicit v2 envelope, deterministic MJCF, atomic rollback, architecture/build boundaries |
| Medium | Fast + local MuJoCo and mesh tools | compiled parity, multi-step dynamics, backend/cache/incremental behavior, invalid input/protocol/dependencies, budget, timeout/cancellation/cleanup faults |
| SolidWorksIntegration | dedicated Windows + SW2025 SDK/server | all regressions and mocked UI/storage lifecycle; new CAD transforms/mass/STL, timestep-only counts, warm/cold physical parity, copied immutable v2 CAD save/reopen/export |
| Release | dedicated idle elevated CAD runner + Inno Setup | mandatory tests and hashed validation receipt, payload gate, installer, installed binary hashes, COM addin load, owned assembly save, uninstall and registration cleanup |

No tier opens a user project. CAD fixtures are newly created or copied from frozen input files. An unexpected open document stops all writes. Release never kills SolidWorks, bypasses its installer guard, or uninstalls the old plugin.

`build.ps1 -Package/-Installer` runs mandatory regression before assembly. `AssembleRelease.ps1` rejects absent validation receipts or production DLL/backend changes after validation. Installer creation does not itself claim install/uninstall or GUI acceptance. Reports/logs remain in build/. Self-hosted release execution is deliberately workflow-dispatch only and requires the `solidworks-release` environment; it must never execute arbitrary fork PR code on a workstation.

GitHub workflow actions follow their upstream interfaces: [checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet), [setup-python](https://github.com/actions/setup-python), [upload-artifact](https://github.com/actions/upload-artifact). Workflow execution on GitHub is separate evidence from local script execution; adding a workflow is not a passing CI run.

The CAD process guard blocks any process with threads, handles, or unknown counts. Windows can retain an enumerable process object with zero threads and zero handles (observed in an owned COM test). This inert object does not block installation. WMI errors still block; the separate COM zero-document guard remains mandatory. The predicate is tested against live, inert and unknown cases and is shared with installer WQL. See [Win32_Process thread/handle properties](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-process).

A first local Medium run reported one reference prepared-cache miss in the combined force/spring/equality acceptance case. The unchanged strict test passed its isolated rerun and the complete Medium rerun. This is retained as an observed non-reproducible result, not hidden by a tolerance/count change or production retry.
