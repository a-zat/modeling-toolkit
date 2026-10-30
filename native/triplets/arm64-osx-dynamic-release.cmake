# Same as the community arm64-osx-dynamic triplet, but dependencies are built in release only.
# This halves the OpenCASCADE build time; our own code can still be built in Debug.
set(VCPKG_TARGET_ARCHITECTURE arm64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_BUILD_TYPE release)

set(VCPKG_CMAKE_SYSTEM_NAME Darwin)
set(VCPKG_OSX_ARCHITECTURES arm64)
# The macOS SDK of the selected developer directory (xcode-select), like CMAKE_OSX_SYSROOT in CMakePresets.json:
# when both Xcode and the command line tools are installed, xcrun's default SDK may come from the other one.
set(VCPKG_OSX_SYSROOT macosx)

# Precompiled headers: OpenCASCADE's CI builds Release + Debug on a 4-vCPU Windows runner in 30 minutes with them and
# in 88 without them.
if(PORT STREQUAL "opencascade")
    set(VCPKG_CMAKE_CONFIGURE_OPTIONS -DBUILD_USE_PCH=ON)
endif()
