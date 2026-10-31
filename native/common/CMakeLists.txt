# Status codes of the C APIs: the public headers of the libraries include this header.
add_library(mtk_status INTERFACE)
target_include_directories(mtk_status INTERFACE include)

# Conversion of exceptions into status codes. A static library: each shared library links its own copy, with the
# message of its own last error.
add_library(mtk_errors STATIC src/errors.cpp)
target_include_directories(mtk_errors PUBLIC src)
target_include_directories(mtk_errors SYSTEM PUBLIC ${OpenCASCADE_INCLUDE_DIR})
target_link_libraries(mtk_errors PUBLIC mtk_status)
set_target_properties(mtk_errors PROPERTIES POSITION_INDEPENDENT_CODE ON)
mtk_set_compile_options(mtk_errors)
