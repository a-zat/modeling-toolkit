find_package(OpenCASCADE CONFIG REQUIRED)

add_library(mtk_occt_bridge SHARED
    src/bridge.cpp
    src/scene.cpp
    src/shape.cpp)

target_include_directories(mtk_occt_bridge PUBLIC include)
target_include_directories(mtk_occt_bridge SYSTEM PRIVATE ${OpenCASCADE_INCLUDE_DIR})
target_compile_definitions(mtk_occt_bridge PRIVATE MTK_OCCT_BRIDGE_EXPORTS)
target_link_libraries(mtk_occt_bridge PRIVATE
    # Geometry and topology
    TKernel TKMath TKBRep TKGeomBase TKPrim TKTopAlgo
    # XDE documents (assemblies, names, materials) and the glTF reader
    TKLCAF TKCAF TKXCAF TKService TKRWMesh TKDEGLTF)
mtk_set_compile_options(mtk_occt_bridge)

if(APPLE)
    # Also look for dependencies next to the bridge, so the copy in the .NET output folder is self-contained.
    set_target_properties(mtk_occt_bridge PROPERTIES
        BUILD_RPATH "@loader_path"
        INSTALL_RPATH "@loader_path")

    # On Windows vcpkg copies the dependent DLLs automatically (VCPKG_APPLOCAL_DEPS); on macOS we do it ourselves.
    add_custom_command(TARGET mtk_occt_bridge POST_BUILD
        COMMAND ${CMAKE_COMMAND}
            -D "LIBRARY=$<TARGET_FILE:mtk_occt_bridge>"
            -D "DESTINATION=$<TARGET_FILE_DIR:mtk_occt_bridge>"
            -P "${PROJECT_SOURCE_DIR}/cmake/CopyRuntimeDependencies.cmake"
        VERBATIM)
endif()

if(BUILD_TESTING)
    add_subdirectory(tests)
endif()
