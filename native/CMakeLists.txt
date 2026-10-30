cmake_minimum_required(VERSION 3.28)

project(ModelingToolkitNative
    VERSION 0.1.0
    DESCRIPTION "Native components of Modeling Toolkit"
    LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Only symbols explicitly marked for export are visible outside our shared libraries.
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

# Dependencies are built in release only (see the vcpkg triplets): use the matching C runtime in every configuration.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreadedDLL")

# Every runtime file (our libraries, test executables, copied dependencies) goes to one folder per configuration,
# which the .NET build copies next to the managed assemblies.
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${PROJECT_BINARY_DIR}/bin/$<CONFIG>")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${PROJECT_BINARY_DIR}/bin/$<CONFIG>")

include(CTest)

function(mtk_set_compile_options target)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
    endif()
endfunction()

add_subdirectory(occt-bridge)

# Tells the .NET build (NativeBridge.targets) that the first configure completed: CMakeCache.txt alone is not enough,
# because CMake writes it even when configuring fails.
file(TOUCH "${PROJECT_BINARY_DIR}/configure.stamp")
