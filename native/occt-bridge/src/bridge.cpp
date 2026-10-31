#include "internal.hpp"

#include <Standard_Version.hxx>

int32_t mtk_bridge_abi_version(void) {
    return MTK_OCCT_BRIDGE_ABI_VERSION;
}

const char* mtk_occt_version(void) {
    return OCC_VERSION_COMPLETE;
}

const char* mtk_last_error_message(void) {
    return mtk::last_error_message();
}
