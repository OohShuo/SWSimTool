# Modernization baseline and product identity acceptance

This matrix freezes the existing behavior; physical tolerances remain unchanged.

| Capability | Automated evidence |
|---|---|
| URDF export | Core root serialization + frozen v2 XML |
| Native MJCF export | native CAD incremental + compiled parity |
| mesh preparation | baseline backend 96 plus binding regression; tool contract 38 |
| simplification | backend budget/dependency/cache tests |
| validation | compiled parity + tool faults |
| preview | backend preview contract; GUI NOT PERFORMED |
| incremental export | native timestep-only 0/0/0/1/1 + warm/cold dynamics |
| cache | identity warm/cold and backend invalidation |
| site | parity + stable reference 297 |
| collision | collision math + old/new CAD physics |
| contact | old/new CAD compiled semantics |
| equality | parity + frozen connect |
| actuator | parity + frozen motor |
| sensor | parity + frozen IMU |
| constant force | parity + frozen force |
| spring | parity + frozen spring |
| solver | parity + timestep-only + frozen custom solver |
| SolidWorks persistence | 15 serialization + actual CAD save/reopen |
| installer | compile/install/uninstall/reinstall + old DLL hash unchanged |
| COM registration | installed LoadAddIn + owned assembly creation/save |

Compatibility sources: `tests/compatibility/sw2mujoco_v2` and immutable `tests/fixtures/sw2mujoco-v2`. Their provenance identifies the pre-rename generator. CAD fixtures are always copied before opening. Rename/delete/recreate identity behavior is covered by the 297-case lifecycle gate and the native CAD identity gate.

Known differences: product-generated internal names use `swsimtool_` instead of `sw2mujoco_`; only this prefix is aliased for the old/new physical comparison. Old and new exports must use the same saved configuration (the earlier in-memory timestep export is not that reference). A previously destructive lifecycle fixture intentionally contained dangling references and was unsuitable for the valid-model export comparison.

GUI visual acceptance: **NOT PERFORMED**. Actual SolidWorks COM, save/reopen, native export, installation, registration and MuJoCo physics acceptance were performed using owned fixtures. No user engineering document was opened.
