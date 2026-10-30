#include "internal.hpp"

#include <Standard_Version.hxx>

namespace {

thread_local std::string last_error;

} // namespace

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

int32_t mtk_bridge_abi_version(void) {
    return MTK_OCCT_BRIDGE_ABI_VERSION;
}

const char* mtk_occt_version(void) {
    return OCC_VERSION_COMPLETE;
}

const char* mtk_last_error_message(void) {
    return last_error.c_str();
}
