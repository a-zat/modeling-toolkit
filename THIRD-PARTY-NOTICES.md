# Third-party notices

The repository includes the following third-party material. The sample models in `tests/assets` have their own
notices in [tests/assets/README.md](tests/assets/README.md).

## Fluent UI System Icons

The icons in `src/ModelingToolkit.Gui/Styles/Icons.axaml` are converted from the SVG files of
[Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons).

```text
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Components of the applications

The desktop application and the command line tool are built with the components below, which the binaries contain.
Their licences allow that, and nearly all ask for their copyright notice and licence text to go with the binaries:
these are collected when the binaries are packaged, from the copyright files that vcpkg installs and the licence files
of the NuGet packages. Versions are those of `Directory.Packages.props` and of the vcpkg checkout in `external/vcpkg`.

### Native libraries (vcpkg)

| Component | Version | Licence | Used for |
|---|---|---|---|
| [OpenCASCADE Technology](https://github.com/Open-Cascade-SAS/OCCT) | 8.0.1, with the patches of `native/ports/opencascade` | LGPL-2.1-only with the Open CASCADE exception | Geometry kernel, glTF import, 3D viewer |
| [FreeType](https://freetype.org) | 2.14.3 | FTL (chosen over GPL-2.0-or-later) | Text of the 3D viewer, through OpenCASCADE |
| [Draco](https://github.com/google/draco) | 1.5.7 | Apache-2.0 | Compressed glTF meshes, through OpenCASCADE |
| [RapidJSON](https://rapidjson.org) | 2025-02-26 snapshot | MIT | glTF parsing, through OpenCASCADE (compiled in) |
| [libpng](http://www.libpng.org) | 1.6.59 | libpng-2.0 | Images, through FreeType |
| [zlib](https://zlib.net) | 1.3.2 | Zlib | Compression, through libpng and FreeType |
| [bzip2](https://sourceware.org/bzip2/) | 1.0.8 | bzip2-1.0.6 | Compression, through FreeType |
| [Brotli](https://github.com/google/brotli) | 1.2.0 | MIT | Compressed fonts, through FreeType |

OpenCASCADE is linked dynamically and ships as separate libraries, which a user can replace with others built from
its sources, as the LGPL requires. Its sources, and the patches applied to them, are those of the vcpkg port in
`native/ports/opencascade`; a binary distribution has to offer them with the binaries.

### .NET packages (NuGet)

| Component | Version | Licence | Used for |
|---|---|---|---|
| [Avalonia](https://avaloniaui.net) (and its platform packages) | 12.1.4 | MIT | Desktop user interface |
| [SkiaSharp](https://github.com/mono/SkiaSharp), with Skia | 3.119.4 | MIT; Skia BSD-3-Clause | Drawing of Avalonia |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp), with HarfBuzz | 8.3.1.3 | MIT; HarfBuzz MIT | Text shaping of Avalonia |
| ANGLE, in Avalonia.Angle.Windows.Natives | 2.1.27548 | BSD-3-Clause | OpenGL on Windows, for Avalonia |
| [Inter](https://rsms.me/inter/), in Avalonia.Fonts.Inter | 12.1.4 (package) | OFL-1.1 | Font of the interface |
| MicroCom.Runtime, Tmds.DBus.Protocol | 0.11.6, 0.94.1 | MIT | Platform code of Avalonia |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT | View models |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | 2.0.14 | MIT | Command line parsing |
| Microsoft.Extensions.Hosting and its dependencies | 10.0.14 | MIT | Composition of the applications |

Development only, not in the binaries: GoogleTest (BSD-3-Clause) and xUnit (Apache-2.0) for the tests,
RolandUI.DevScope (MIT) in Debug builds, Avalonia.BuildServices (MIT) at build time.
