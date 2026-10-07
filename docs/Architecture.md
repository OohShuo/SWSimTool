# Project boundaries

All four production projects currently target .NET Framework 4.8. The SolidWorks COM assembly remains `SWSimTool.dll`; its CLSID and on-document v2 schema are unchanged by extraction.

- **Core** owns immutable robot and simulation snapshots, SI transforms/full inertia, editable configuration records, scalar validation and stable-ID mode semantics. It has no CAD, UI, serializer, process or filesystem implementation.
- **Application** owns typed tool contracts, simulation assembly, native CAD source coordination, export planning and the stage/prepare/write/validate/publish workflow. Its native CAD source interface cannot select reference generation. File and package operations are supplied through `IAssetStore` / `IPackageStore`; export generation uses `IMjcfWriter`.
- **Infrastructure** implements deterministic MJCF writing, local URDF/sidecar parsing, strict v2 JSON envelope serialization, raw cache copying/hashing, Python tools/process control and atomic package publication.
- **SolidWorks** owns the addin, menus, UI, tree configuration, COM references, CAD acquisition and STL adapters, document/configuration lifetime, and attribute transport. It translates CAD objects into resolved data before calling Application/Core. Session cache validity remains tied to the live CAD document; concrete cache file operations belong to Infrastructure.

`IDocumentStore` transports an opaque configuration string. The SolidWorks attribute adapter retains write/read-back verification and rollback. The common envelope serializer enforces explicit v2; URDF DataContract namespaces retain the frozen ABI. Native production does not silently use the Python reference generator.

The independent parity runner references the actual reusable projects rather than compiling copies of production source. `tests/architecture/Test-LayeredArchitecture.py` enforces the project graph, CAD/UI/process/filesystem boundaries, native-only production contract and compile-before-publication ordering.

The projects use SDK-style MSBuild and pinned PackageReference dependencies. `Version.props` is the product-version authority; common build properties isolate each project/configuration/framework below build/. The net48 COM host remains unchanged. Source archives restore from NuGet without repository packages or vendored log4net. Two independent source-archive builds produced identical payload file sets, metadata and all eight DLL hashes; deterministic MJCF and strict compiled/dynamics parity passed. Old dependency versions are retained for this migration, including NuGet-audited log4net 2.0.8.
