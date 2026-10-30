# Same as the x64-windows-release triplet that comes with vcpkg (this file takes precedence over it), plus precompiled
# headers for OpenCASCADE.
set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_BUILD_TYPE release)
set(VCPKG_PROVIDED_FORTRAN ON)

# Precompiled headers: OpenCASCADE's CI builds Release + Debug on a 4-vCPU Windows runner in 30 minutes with them and
# in 88 without them.
if(PORT STREQUAL "opencascade")
    set(VCPKG_CMAKE_CONFIGURE_OPTIONS -DBUILD_USE_PCH=ON)
endif()
