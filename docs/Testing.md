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

## Portable platform gate

On Windows or Linux with a .NET SDK, Python/MuJoCo, fast-simplification and PyMeshLab (Linux also needs libopengl0 for its CPU meshing plugin):

```sh
python -B tests/platform/run.py
```

This builds the actual net8 CLI and reusable layers, exercises frozen storage, Unicode CLI paths, typed tool faults, backend regressions and all 14 native parity suites under en-US, zh-CN and de-DE. Windows additionally compares net48 storage; Linux checks case-distinct mesh mutation detection, unrelated case-distinct package files and symlink refusal. Timeout, cancellation and normal root exit with a pipe-owning child all require owned-tree termination. A Linux zombie is non-executing and is reported as terminated; live children are never accepted.

Output is isolated below `build/reports/platform/<system>/`. Repeated runs must archive or remove their own previous culture output first; stale evidence cannot silently pass. Copy both OS output sets to the same platform directory, then run:

```sh
python -B tests/platform/compare.py
```

The comparator requires all six OS/culture sets and exact frozen raw/typed storage equality. It compiles every captured candidate fixture and applies the existing 1e-9 structural/physical tolerance and 2e-7 dynamic tolerance at 1/10/100 steps. XML structure, ordering and reference names must match; numeric attributes use the existing 1e-9 bound. Within each OS, all three cultures must produce identical XML after newline normalization. Cross-OS text differences are recorded: actual Linux/Windows runs showed last-bit trigonometric and inertia rounding differences (for example 0.7071067811865475 versus 0.7071067811865476). Compiled semantics and dynamics are the acceptance gates, rather than cross-OS byte equality. No physical tolerance is widened for Linux. CI uses the same MuJoCo/numpy versions on both platforms; these are test environment pins, not runtime restrictions.

The first local platform run hit a Windows file-lock error while the test-only Python reference repeatedly replaced a shared scenario output. Semantic scenarios now use independent reference directories; package overwrite/cache/rollback coverage remains in its separate lifecycle regression. Production gets no retry and assertions are unchanged.

Real Linux tests exposed asymmetric MuJoCo binding-enum equality with NumPy scalars: direct equality succeeded but tuple membership failed. Diagnostic connect/weld type filtering now compares integer values, with an explicit asymmetric-binding regression. The parity helper uses the same explicit conversion so hinge/slide perturbations are exercised on both systems; this strengthens the existing dynamics tests rather than reducing their coverage or tolerances. Full backend tests require both Python simplifiers even though each is optional for production; CI installs both explicitly.

The build test entry point sets both backend caches below `build/test-work`. A direct sandbox run without these overrides stalled in Python's Windows temporary-directory loop against an inaccessible existing user cache; the isolated full rerun passed all 97 tests. Test execution must not depend on, or populate, a user's production cache.
