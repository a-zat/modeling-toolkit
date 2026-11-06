# modeling-toolkit

A versatile toolkit for importing, processing, modifying, and exporting 3D models across multiple formats.
It handles meshes as well as exact geometry (B-rep solids, NURBS, analytic primitives), with a C# core and
[OpenCASCADE](https://github.com/Open-Cascade-SAS/OCCT) as geometry kernel.

> **Status:** version 0.1.0, a viewer: see the [changelog](CHANGELOG.md). The `mtk` command line tool reads glTF models (`.gltf`, `.glb`, with meshes
> compressed by Draco and textures in WebP too) and writes them in the native `.mtk` format, keeping shared geometry,
> hierarchy, materials and textures. It also builds shapes of exact geometry, with their boolean combinations. The
> desktop application opens and saves models, shows them in its 3D view, drawn by OpenCASCADE, and shows their
> structure, materials and properties.

## Download

The [releases](https://github.com/a-zat/modeling-toolkit/releases) have a package for macOS (Apple Silicon) and one
for Windows (x64), with the desktop application and the `mtk` tool: unzip it and run them, nothing needs to be
installed. The application is not signed by a developer identity: on macOS, remove the quarantine mark of the
unzipped folder once with `xattr -dr com.apple.quarantine <folder>`; on Windows, SmartScreen asks to confirm.

## Requirements

To build the toolkit from its sources:

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
the view to it. Clicking a mesh selects it in the model tree, which opens down to it, and clicking the empty space
clears the selection; the mesh under the pointer is tinted. Selecting a node of the tree fades the rest of the model,
so that the selection stands out. The bar at the top fits the model in the view, turns to the standard views and
chooses how the model is drawn: shaded, with its edges, or as a wireframe. The edges of a shape are its own, those of
a mesh its sharp ones, where its faces meet at an angle. The View menu does the
same, and switches to an orthographic projection or hides the grid.

After a build, the tool is `artifacts/bin/ModelingToolkit.Cli/debug/mtk`:

```sh
mtk convert model.glb model.mtk   # the output extension chooses the format
mtk stats model.mtk               # blocks, meshes, triangles, materials, bounds in millimetres
```

Shapes of exact geometry, which the native format keeps as they are, are built with `mtk make` and combined with
`mtk combine`; the application shows their triangles:

```sh
mtk make box 100 100 100 box.mtk                           # also cylinder, sphere, cone and torus
mtk make cylinder 20 120 drill.mtk --at 50,50,-10          # --at puts the shape where it is wanted
mtk combine subtract box.mtk drill.mtk drilled.mtk         # also union and intersect
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

The packages of a release are made by `python3 packaging/package.py`, on the platform they are for, after a build:
it publishes the applications self-contained and collects the licences of what they contain. The release workflow
runs it on Windows when a version is tagged.

## License

The Modeling Toolkit is released under the [MIT licence](LICENSE). Third-party material included in the repository,
and the components that the applications contain with their licences, are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
