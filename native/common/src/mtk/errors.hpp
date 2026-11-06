#pragma once

#include "mtk/status.h"

#include <Standard_Failure.hxx>

#include <exception>
#include <stdexcept>
#include <string>

namespace mtk {

// An operation of OpenCASCADE failed without raising an exception; reported as MTK_STATUS_KERNEL_ERROR.
class kernel_error : public std::runtime_error {
public:
    using std::runtime_error::runtime_error;
};

// The caller asked the operation to stop; reported as MTK_STATUS_CANCELLED.
class cancelled : public std::runtime_error {
public:
    cancelled() : std::runtime_error("The operation was cancelled.") {}
};

// Message of the last error on the calling thread. Each library links its own copy of this code, so it keeps the
// errors of its own calls.
const char* last_error_message() noexcept;
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
    } catch (const cancelled& error) {
        set_last_error(error.what());
        return MTK_STATUS_CANCELLED;
    } catch (const kernel_error& error) {
        set_last_error(error.what());
        return MTK_STATUS_KERNEL_ERROR;
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
