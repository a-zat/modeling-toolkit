# modeling-toolkit

A versatile toolkit for importing, processing, modifying, and exporting 3D models across multiple formats.
It handles meshes as well as exact geometry (B-rep solids, NURBS, analytic primitives), with a C# core and
[OpenCASCADE](https://github.com/Open-Cascade-SAS/OCCT) as geometry kernel.

> **Status:** early development. The `mtk` command line tool reads glTF models (`.gltf`, `.glb`) and writes them
> in the native `.mtk` format, keeping shared geometry, hierarchy, materials and textures. The desktop application
> opens and saves models, shows them in its 3D view, drawn by OpenCASCADE, and shows their structure, materials and
> properties.

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

dotnet build                                          # builds the native libraries too
dotnet test                                           # .NET tests
dotnet run --project src/ModelingToolkit.Cli -- info  # prints the toolkit and kernel versions
dotnet run --project src/ModelingToolkit.Gui          # desktop application
```

The desktop application has a light and a dark theme, switched at the bottom right of the status bar; the tests render
both into `artifacts/screenshots`, without the 3D view, which is a native view. In Debug builds, F12 opens DevScope, an
inspector of the visual tree and of the styles.

The 3D view shows the ground in perspective, with a grid of squares of one metre whose X and Y axes, red and green,
cross at the origin. The left button rotates, the middle button or Shift with the left button pans, and the wheel, a
pinch or the right button zooms; the status bar recalls it. Clicking a face of the cube in the upper right corner turns
the view to it. Selecting a node of the model tree fades the rest of the model, so that the selection stands out.

After a build, the tool is `artifacts/bin/ModelingToolkit.Cli/debug/mtk`:

```sh
mtk convert model.glb model.mtk   # the output extension chooses the format
mtk stats model.mtk               # blocks, meshes, triangles, materials, bounds in millimetres
```

The first build compiles OpenCASCADE through vcpkg (about 7 minutes on an Apple M2); later builds reuse its cache.
In an existing clone without the `external/vcpkg` submodule, run `git submodule update --init` first.

The C++ tests of the native libraries run with CTest, from the `native` folder:

```sh
ctest --preset macos-debug     # or windows-debug
```

## Repository layout

| Path | Content |
|---|---|
| `src/` | .NET projects: `Core` (domain model), `Kernel.Occt` (OpenCASCADE and glTF import), `IO.Native` (`.mtk` format), `Cli` (`mtk` tool), `Gui` (desktop application, Avalonia) |
| `tests/` | .NET tests (xUnit) and the sample models they use (`tests/assets`) |
| `native/` | C++ projects built with CMake: `occt-bridge`, the C API over OpenCASCADE; `occt-viewer`, the OpenCASCADE view of the desktop application; `common`, what they share |
| `external/vcpkg` | vcpkg, the C++ package manager, as a submodule |
| `artifacts/` | Build outputs (not versioned) |

## Development

Milestones, tasks and bugs are tracked in the [GitHub project](https://github.com/users/a-zat/projects/1).

The repository is set up for VS Code: open the folder and install the recommended extensions. The default build task
(`⇧⌘B`) builds everything, and the launch configurations run the desktop application or the CLI under the .NET
debugger or, for the C++ code, the CLI under LLDB.

Third-party material included in the repository is listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
