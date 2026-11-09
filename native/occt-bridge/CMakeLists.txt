add_library(mtk_occt_bridge SHARED
    src/bridge.cpp
    src/scene.cpp
    src/boolean.cpp
    src/edges.cpp
    src/serialization.cpp
    src/shape.cpp
    src/tessellation.cpp)

target_include_directories(mtk_occt_bridge PUBLIC include)
target_include_directories(mtk_occt_bridge SYSTEM PRIVATE ${OpenCASCADE_INCLUDE_DIR})
target_compile_definitions(mtk_occt_bridge PRIVATE MTK_OCCT_BRIDGE_EXPORTS)
target_link_libraries(mtk_occt_bridge PUBLIC mtk_status)
target_link_libraries(mtk_occt_bridge PRIVATE
    mtk_errors
    # Geometry and topology
    TKernel TKMath TKBRep TKGeomBase TKPrim TKTopAlgo TKMesh TKBO TKBool TKShHealing TKOffset
    # XDE documents (assemblies, names, materials) and the glTF reader
    TKLCAF TKCAF TKXCAF TKService TKRWMesh TKDEGLTF)
mtk_set_compile_options(mtk_occt_bridge)
mtk_copy_runtime_dependencies(mtk_occt_bridge)

if(BUILD_TESTING)
    add_subdirectory(tests)
endif()
