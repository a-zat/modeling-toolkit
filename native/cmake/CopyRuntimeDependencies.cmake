# Copies the shared libraries a binary depends on (resolved through its rpaths) next to it,
# skipping the ones that ship with the operating system.
#
# Usage: cmake -D LIBRARY=<binary> -D DESTINATION=<folder> -P CopyRuntimeDependencies.cmake

file(GET_RUNTIME_DEPENDENCIES
    LIBRARIES "${LIBRARY}"
    RESOLVED_DEPENDENCIES_VAR dependencies
    UNRESOLVED_DEPENDENCIES_VAR unresolved
    POST_EXCLUDE_REGEXES "^/usr/lib/" "^/System/")

if(unresolved)
    message(FATAL_ERROR "Unresolved runtime dependencies of ${LIBRARY}: ${unresolved}")
endif()

file(REAL_PATH "${DESTINATION}" destination)
foreach(dependency IN LISTS dependencies)
    cmake_path(GET dependency PARENT_PATH folder)
    file(REAL_PATH "${folder}" folder)
    # After the first build, dependencies may already resolve to the copies in the destination folder.
    if(NOT folder STREQUAL destination)
        cmake_path(GET dependency FILENAME name)
        file(COPY_FILE "${dependency}" "${destination}/${name}" ONLY_IF_DIFFERENT)
    endif()
endforeach()
