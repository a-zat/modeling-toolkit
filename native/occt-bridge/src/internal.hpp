#pragma once

#include "mtk/occt_bridge.h"

#include <Standard_Failure.hxx>
#include <TopoDS_Shape.hxx>

#include <exception>
#include <string>

// Definition of the opaque handle declared in the public header.
struct mtk_shape {
    TopoDS_Shape value;
};

namespace mtk {

void set_last_error(const std::string& message) noexcept;
void clear_last_error() noexcept;

// Records the error and returns MTK_STATUS_INVALID_ARGUMENT; used for argument checks inside guarded().
mtk_status invalid_argument(const char* message) noexcept;

// Runs the body of an exported function and converts any exception into a status code:
// C++ and OpenCASCADE exceptions must never cross the C boundary.
template <typename Body>
mtk_status guarded(Body&& body) noexcept {
    clear_last_error();
    try {
        return body();
    } catch (const Standard_Failure& failure) {
        // Caught before std::exception, which Standard_Failure derives from.
        set_last_error(std::string(failure.ExceptionType()) + ": " + failure.what());
        return MTK_STATUS_KERNEL_ERROR;
    } catch (const std::exception& exception) {
        set_last_error(exception.what());
        return MTK_STATUS_INTERNAL_ERROR;
    } catch (...) {
        set_last_error("Unknown native exception.");
        return MTK_STATUS_INTERNAL_ERROR;
    }
}

} // namespace mtk
