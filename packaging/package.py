#!/usr/bin/env python3
"""
Packages a release of the desktop application and the command line tool for the platform it runs on: both published
self-contained, with the licence of the toolkit, its third-party notices and the licence texts of every component the
binaries contain. On macOS the application becomes an app bundle, signed ad hoc.

    python3 packaging/package.py [--output DIR]

The result is artifacts/packages/ModelingToolkit-<version>-<platform>.zip, or in DIR.
"""

import argparse
import html
import json
import os
import pathlib
import platform
import plistlib
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent

# The platforms the native code is built for: runtime identifier, vcpkg triplet (native/triplets) and CMake preset.
PLATFORMS = {
    ("Darwin", "arm64"): ("osx-arm64", "arm64-osx-dynamic-release", "macos", "macos-arm64"),
    ("Windows", "AMD64"): ("win-x64", "x64-windows-release", "windows", "windows-x64"),
}

# The vcpkg ports whose libraries the binaries contain (see THIRD-PARTY-NOTICES.md); the others are build tools.
NATIVE_PORTS = ["opencascade", "freetype", "draco", "rapidjson", "libpng", "zlib", "bzip2", "brotli"]

MIT = """Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "artifacts" / "packages")
    arguments = parser.parse_args()

    key = (platform.system(), platform.machine())
    if key not in PLATFORMS:
        sys.exit(f"Packages are made on macOS (arm64) and Windows (x64), not on {key[0]} ({key[1]}).")
    rid, triplet, preset, name = PLATFORMS[key]
    version = read_version()

    package = f"ModelingToolkit-{version}-{name}"
    staging = arguments.output / package
    shutil.rmtree(staging, ignore_errors=True)
    staging.mkdir(parents=True)

    application = publish("ModelingToolkit.Gui", rid, staging / "app")
    command_line = publish("ModelingToolkit.Cli", rid, staging / "cli")

    shutil.copy(ROOT / "LICENSE", staging / "LICENSE.txt")
    shutil.copy(ROOT / "THIRD-PARTY-NOTICES.md", staging / "THIRD-PARTY-NOTICES.md")
    licences = staging / "licenses"
    collect_native_licences(licences / "native", preset, triplet)
    collect_package_licences(licences / "nuget", [application, command_line])
    shutil.copy(ROOT / "packaging" / "licenses" / "Inter.txt", licences / "Inter-font.txt")
    collect_opencascade_sources(licences / "opencascade-sources")

    if preset == "macos":
        bundle = make_app_bundle(application, staging / "Modeling Toolkit.app", version)
        sign_ad_hoc(bundle)
        sign_ad_hoc(command_line)
    write_readme(staging / "README.txt", version, preset)

    archive = arguments.output / f"{package}.zip"
    archive.unlink(missing_ok=True)
    make_zip(staging, archive, preset)
    print(archive)


def read_version():
    text = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    match = re.search(r"<Version>([^<]+)</Version>", text)
    if match is None:
        sys.exit("Directory.Build.props has no <Version>.")
    return match.group(1)


def publish(project, rid, output):
    run(["dotnet", "publish", str(ROOT / "src" / project), "--configuration", "Release", "--runtime", rid,
         "--self-contained", "--output", str(output), "-p:DebugType=none", "-p:DebugSymbols=false"])
    return output


def collect_native_licences(destination, preset, triplet):
    share = ROOT / "artifacts" / "native" / preset / "vcpkg_installed" / triplet / "share"
    destination.mkdir(parents=True)
    for port in NATIVE_PORTS:
        copyright_file = share / port / "copyright"
        if not copyright_file.is_file():
            sys.exit(f"{copyright_file} is missing: build the native code first.")
        shutil.copy(copyright_file, destination / f"{port}.txt")


def collect_package_licences(destination, published):
    """The licence files of the NuGet packages listed by the deps.json files of the published applications, or, for
    the packages that only state an MIT licence expression, the MIT licence with their copyright."""
    packages_folder = pathlib.Path(os.environ.get("NUGET_PACKAGES", pathlib.Path.home() / ".nuget" / "packages"))
    destination.mkdir(parents=True)
    seen = set()
    for folder in published:
        for deps in folder.glob("*.deps.json"):
            libraries = json.loads(deps.read_text(encoding="utf-8"))["libraries"]
            for library, entry in libraries.items():
                if entry["type"] not in ("package", "runtimepack") or library in seen:
                    continue
                seen.add(library)
                package_id, package_version = library.removeprefix("runtimepack.").split("/")
                path = entry.get("path") or f"{package_id.lower()}/{package_version}"
                write_package_licence(packages_folder / path, package_id, package_version,
                                      destination / f"{package_id}.txt")


def write_package_licence(folder, package_id, package_version, target):
    nuspec = next(folder.glob("*.nuspec"), None)
    if nuspec is None:
        sys.exit(f"The package {package_id} {package_version} is not in {folder}.")
    spec = html.unescape(nuspec.read_text(encoding="utf-8"))
    licence = re.search(r"<license[^>]*>([^<]+)</license>", spec)
    copyright_line = re.search(r"<copyright>([^<]+)</copyright>", spec)
    project_url = re.search(r"<projectUrl>([^<]+)</projectUrl>", spec)

    parts = [f"{package_id} {package_version}"]
    if project_url:
        parts.append(project_url.group(1))
    if licence:
        parts.append(f"Licence: {licence.group(1)}")
    texts = sorted(file for file in folder.iterdir()
                   if file.is_file() and re.search(r"licen[cs]e|notice|copying", file.name, re.IGNORECASE))
    for text in texts:
        parts.append(f"----- {text.name} -----\n{text.read_text(encoding='utf-8', errors='replace').strip()}")
    if not texts:
        if not licence or licence.group(1) != "MIT":
            sys.exit(f"{package_id} has no licence file and no MIT licence expression: add its licence by hand.")
        holder = copyright_line.group(1) if copyright_line else f"The authors of {package_id}"
        parts.append(f"{holder}\n\n{MIT.strip()}")
    target.write_text("\n\n".join(parts) + "\n", encoding="utf-8")


def collect_opencascade_sources(destination):
    """The LGPL asks for the sources of OpenCASCADE as built: the release named by the vcpkg port, with its patches."""
    shutil.copytree(ROOT / "native" / "ports" / "opencascade", destination)
    (destination / "README.txt").write_text(
        "The OpenCASCADE libraries of this package are built from the sources of OpenCASCADE Technology 8.0.1,\n"
        "https://github.com/Open-Cascade-SAS/OCCT, at the version and checksum that portfile.cmake names, with the\n"
        "patches of this folder applied, by vcpkg. They are licensed under the LGPL 2.1 with the Open CASCADE\n"
        "exception (see ../native/opencascade.txt) and are separate libraries, which can be replaced with others\n"
        "built from these sources.\n", encoding="utf-8")


def make_app_bundle(application, bundle, version):
    """Moves the published application into a macOS app bundle."""
    contents = bundle / "Contents"
    contents.mkdir(parents=True)
    shutil.move(str(application), str(contents / "MacOS"))
    with open(contents / "Info.plist", "wb") as plist:
        plistlib.dump({
            "CFBundleName": "Modeling Toolkit",
            "CFBundleDisplayName": "Modeling Toolkit",
            "CFBundleIdentifier": "io.github.a-zat.modeling-toolkit",
            "CFBundleExecutable": "ModelingToolkit.Gui",
            "CFBundlePackageType": "APPL",
            "CFBundleShortVersionString": version,
            "CFBundleVersion": version,
            "NSHighResolutionCapable": True,
            "NSHumanReadableCopyright": "Copyright (c) 2026 Andrea Zatelli. MIT licence.",
        }, plist)
    return bundle


def sign_ad_hoc(path):
    """Apple silicon runs only signed code: an ad hoc signature, without a developer identity, is enough to run it. A
    bundle is signed as a whole, the executables and libraries of a folder one by one."""
    if path.suffix == ".app":
        run(["codesign", "--force", "--deep", "--sign", "-", str(path)])
        return
    for file in sorted(path.rglob("*")):
        if file.is_file() and not file.is_symlink() and is_mach_o(file):
            run(["codesign", "--force", "--sign", "-", str(file)])


def is_mach_o(file):
    with open(file, "rb") as stream:
        magic = stream.read(4)
    return magic in (b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca")


def write_readme(target, version, preset):
    if preset == "macos":
        start = (
            "Modeling Toolkit.app is the desktop application, cli/mtk the command line tool.\n\n"
            "The application is not notarized by Apple: macOS refuses to open it once downloaded. Remove the\n"
            "quarantine mark from the folder once, in the Terminal:\n\n"
            "    xattr -dr com.apple.quarantine <this folder>\n")
    else:
        start = (
            "app\\ModelingToolkit.Gui.exe is the desktop application, cli\\mtk.exe the command line tool.\n\n"
            "The application is not signed: Windows SmartScreen may ask to confirm that it should run\n"
            "(More info > Run anyway).\n")
    target.write_text(
        f"Modeling Toolkit {version}\n\n{start}\n"
        "Nothing needs to be installed: .NET comes with the application. LICENSE.txt is the licence of the\n"
        "toolkit; THIRD-PARTY-NOTICES.md lists the components it contains and licenses/ holds their licences.\n",
        encoding="utf-8")


def make_zip(staging, archive, preset):
    if preset == "macos":
        # ditto keeps the symbolic links, permissions and signatures of the bundle, which zipfile would lose.
        run(["ditto", "-c", "-k", "--keepParent", str(staging), str(archive)])
        return
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zip_file:
        for file in sorted(staging.rglob("*")):
            if file.is_file():
                zip_file.write(file, pathlib.Path(staging.name) / file.relative_to(staging))


def run(command):
    print("+", " ".join(command), flush=True)
    subprocess.run(command, check=True)


if __name__ == "__main__":
    main()
