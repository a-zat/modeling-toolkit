# Copies the shared libraries a binary depends on next to it, skipping the ones that ship with the operating system.
# They are looked for in SEARCH_DIRECTORY, from a copy of the binary in WORK_DIRECTORY: next to the binary itself
# (@loader_path) they would be the copies of a previous build, which would never be updated.
#
# Usage: cmake -D LIBRARY=<binary> -D DESTINATION=<folder> -D SEARCH_DIRECTORY=<folder> -D WORK_DIRECTORY=<folder>
#              -P CopyRuntimeDependencies.cmake

cmake_path(GET LIBRARY FILENAME library_name)
file(REMOVE_RECURSE "${WORK_DIRECTORY}")
file(MAKE_DIRECTORY "${WORK_DIRECTORY}")
file(COPY_FILE "${LIBRARY}" "${WORK_DIRECTORY}/${library_name}")

file(GET_RUNTIME_DEPENDENCIES
    LIBRARIES "${WORK_DIRECTORY}/${library_name}"
    DIRECTORIES "${SEARCH_DIRECTORY}"
    RESOLVED_DEPENDENCIES_VAR dependencies
    UNRESOLVED_DEPENDENCIES_VAR unresolved
    POST_EXCLUDE_REGEXES "^/usr/lib/" "^/System/")

if(unresolved)
    message(FATAL_ERROR "Unresolved runtime dependencies of ${LIBRARY}: ${unresolved}")
endif()

foreach(dependency IN LISTS dependencies)
    cmake_path(GET dependency FILENAME name)
    file(COPY_FILE "${dependency}" "${DESTINATION}/${name}" ONLY_IF_DIFFERENT)
endforeach()
