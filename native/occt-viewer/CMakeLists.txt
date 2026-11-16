# A library of its own, next to the bridge: the command line tool loads the bridge, and never OpenGL.
add_library(mtk_occt_viewer SHARED src/viewer.cpp src/scene.cpp src/overlay.cpp src/sky.cpp)

if(APPLE)
    target_sources(mtk_occt_viewer PRIVATE src/native_view_cocoa.mm)
    set_source_files_properties(src/native_view_cocoa.mm PROPERTIES COMPILE_OPTIONS -fobjc-arc)
    target_link_libraries(mtk_occt_viewer PRIVATE "-framework AppKit")
elseif(WIN32)
    target_sources(mtk_occt_viewer PRIVATE src/native_view_win32.cpp)
endif()

target_include_directories(mtk_occt_viewer PUBLIC include)
target_include_directories(mtk_occt_viewer SYSTEM PRIVATE ${OpenCASCADE_INCLUDE_DIR})
target_compile_definitions(mtk_occt_viewer PRIVATE MTK_OCCT_VIEWER_EXPORTS)
target_link_libraries(mtk_occt_viewer PUBLIC mtk_status)
target_link_libraries(mtk_occt_viewer PRIVATE
    mtk_errors
    # Viewer and interactive objects, drawn with OpenGL, and the curves of the axes
    TKernel TKMath TKG3d TKService TKV3d TKOpenGl)
mtk_set_compile_options(mtk_occt_viewer)
mtk_copy_runtime_dependencies(mtk_occt_viewer)

if(BUILD_TESTING)
    add_subdirectory(tests)
endif()
