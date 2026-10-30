# modeling-toolkit

A versatile toolkit for importing, processing, modifying, and exporting 3D models across multiple formats.
It handles meshes as well as exact geometry (B-rep solids, NURBS, analytic primitives), with a C# core and
[OpenCASCADE](https://github.com/Open-Cascade-SAS/OCCT) as geometry kernel.

> **Status:** early development. The `mtk` command line tool reads glTF models (`.gltf`, `.glb`) and writes them
> in the native `.mtk` format, keeping shared geometry, hierarchy, materials and textures. A desktop application with
> a 3D viewport is the next milestone.

## Requirements

| | macOS (Apple Silicon) | Windows (x64) |
|---|---|---|
| .NET | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) |
| C++ compiler | Xcode command line tools matching the macOS version | Visual Studio 2022 or later, "Desktop development with C++" workload |
| Build tools | CMake 3.28+ and Ninja (`brew install cmake ninja`) | CMake 3.28+ on the `PATH` |

## Getting started

```sh
git clone --recursive https://github.com/a-zat/modeling-toolkit.git
cd modeling-toolkit

dotnet build                                          # builds the native bridge too
dotnet test                                           # .NET tests
dotnet run --project src/ModelingToolkit.Cli -- info  # prints the toolkit and kernel versions
```

After a build, the tool is `artifacts/bin/ModelingToolkit.Cli/debug/mtk`:

```sh
mtk convert model.glb model.mtk   # the output extension chooses the format
mtk stats model.mtk               # blocks, meshes, triangles, materials, bounds in millimetres
```

The first build compiles OpenCASCADE through vcpkg (about 7 minutes on an Apple M2); later builds reuse its cache.
In an existing clone without the `external/vcpkg` submodule, run `git submodule update --init` first.

The C++ tests of the native bridge run with CTest, from the `native` folder:

```sh
ctest --preset macos-debug     # or windows-debug
```

## Repository layout

| Path | Content |
|---|---|
| `src/` | .NET projects: `Core` (domain model), `Kernel.Occt` (OpenCASCADE and glTF import), `IO.Native` (`.mtk` format), `Cli` (`mtk` tool) |
| `tests/` | .NET tests (xUnit) and the sample models they use (`tests/assets`) |
| `native/` | C++ projects built with CMake: `occt-bridge`, the C API over OpenCASCADE |
| `external/vcpkg` | vcpkg, the C++ package manager, as a submodule |
| `artifacts/` | Build outputs (not versioned) |

## Development

Milestones, tasks and bugs are tracked in the [GitHub project](https://github.com/users/a-zat/projects/1).

The repository is set up for VS Code: open the folder and install the recommended extensions. The default build task
(`⇧⌘B`) builds everything, and the launch configurations run the CLI under the .NET debugger or, for the C++ code,
under LLDB.
