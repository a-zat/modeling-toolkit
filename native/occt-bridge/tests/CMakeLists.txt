find_package(GTest CONFIG REQUIRED)
find_package(Threads REQUIRED)
include(GoogleTest)

add_executable(mtk_occt_bridge_tests
    bridge_tests.cpp
    edges_tests.cpp
    header_is_valid_c.c
    scene_tests.cpp)

target_link_libraries(mtk_occt_bridge_tests PRIVATE mtk_occt_bridge GTest::gtest_main Threads::Threads)
mtk_set_compile_options(mtk_occt_bridge_tests)

cmake_path(SET test_assets_dir NORMALIZE "${PROJECT_SOURCE_DIR}/../tests/assets")
target_compile_definitions(mtk_occt_bridge_tests PRIVATE MTK_TEST_ASSETS_DIR="${test_assets_dir}")

# Tests are listed when CTest runs, not at build time, so the build never has to execute the test binary.
gtest_discover_tests(mtk_occt_bridge_tests DISCOVERY_MODE PRE_TEST)
