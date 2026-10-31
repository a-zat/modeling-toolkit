#include "mtk/errors.hpp"

namespace {

thread_local std::string last_error;

} // namespace

const char* mtk::last_error_message() noexcept {
    return last_error.c_str();
}

void mtk::set_last_error(const std::string& message) noexcept {
    try {
        last_error = message;
    } catch (...) {
        // Out of memory while storing the message: keep the status code, lose the text.
        last_error.clear();
    }
}

void mtk::clear_last_error() noexcept {
    last_error.clear();
}

mtk_status mtk::invalid_argument(const char* message) noexcept {
    set_last_error(message);
    return MTK_STATUS_INVALID_ARGUMENT;
}
