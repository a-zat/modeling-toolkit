cmake_minimum_required(VERSION 3.28)

project(ModelingToolkitNative
    VERSION 0.1.0
    DESCRIPTION "Native components of Modeling Toolkit"
    LANGUAGES C CXX)

# The native view of the viewer on macOS is written in Objective-C++.
if(APPLE)
    enable_language(OBJCXX)
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Only symbols explicitly marked for export are visible outside our shared libraries.
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_OBJCXX_VISIBILITY_PRESET hidden)
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

# Makes a shared library self-contained in the output folder, so the copy in the .NET output folder is too.
# On Windows vcpkg copies the dependent DLLs automatically (VCPKG_APPLOCAL_DEPS); on macOS the library looks for its
# dependencies next to itself, and they are copied there.
function(mtk_copy_runtime_dependencies target)
    if(APPLE)
        set_target_properties(${target} PROPERTIES
            BUILD_RPATH "@loader_path"
            INSTALL_RPATH "@loader_path")
        add_custom_command(TARGET ${target} POST_BUILD
            COMMAND ${CMAKE_COMMAND}
                -D "LIBRARY=$<TARGET_FILE:${target}>"
                -D "DESTINATION=$<TARGET_FILE_DIR:${target}>"
                -P "${PROJECT_SOURCE_DIR}/cmake/CopyRuntimeDependencies.cmake"
            VERBATIM)
    endif()
endfunction()

find_package(OpenCASCADE CONFIG REQUIRED)

add_subdirectory(common)
add_subdirectory(occt-bridge)
add_subdirectory(occt-viewer)

# Tells the .NET build (Native.targets) that the first configure completed: CMakeCache.txt alone is not enough,
# because CMake writes it even when configuring fails.
file(TOUCH "${PROJECT_BINARY_DIR}/configure.stamp")
