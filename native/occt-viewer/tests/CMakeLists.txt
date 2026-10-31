find_package(GTest CONFIG REQUIRED)
include(GoogleTest)

add_executable(mtk_occt_viewer_tests
    header_is_valid_c.c
    viewer_tests.cpp)

target_link_libraries(mtk_occt_viewer_tests PRIVATE mtk_occt_viewer GTest::gtest_main)
mtk_set_compile_options(mtk_occt_viewer_tests)

# Tests are listed when CTest runs, not at build time, so the build never has to execute the test binary.
gtest_discover_tests(mtk_occt_viewer_tests DISCOVERY_MODE PRE_TEST)
