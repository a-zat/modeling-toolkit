/*
 * C API of the Modeling Toolkit bridge to OpenCASCADE.
 *
 * Conventions:
 *  - functions that can fail return an mtk_status; on failure mtk_last_error_message()
 *    describes the error and output parameters are set to zero/null;
 *  - objects created by the bridge are opaque handles owned by the caller,
 *    to be released with the matching *_release function;
 *  - lengths are in millimetres.
 */
#ifndef MTK_OCCT_BRIDGE_H
#define MTK_OCCT_BRIDGE_H

#include <stdint.h>

#if defined(_WIN32)
#  if defined(MTK_OCCT_BRIDGE_EXPORTS)
#    define MTK_API __declspec(dllexport)
#  else
#    define MTK_API __declspec(dllimport)
#  endif
#else
#  define MTK_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Incremented on every breaking change of this API; the managed side checks it when loading the library. */
#define MTK_OCCT_BRIDGE_ABI_VERSION 1

typedef enum mtk_status {
    MTK_STATUS_OK = 0,
    MTK_STATUS_INVALID_ARGUMENT = 1, /* the caller passed an invalid value */
    MTK_STATUS_KERNEL_ERROR = 2,     /* OpenCASCADE raised an exception */
    MTK_STATUS_INTERNAL_ERROR = 3    /* any other unexpected failure */
} mtk_status;

/* Opaque handle to an OpenCASCADE shape. */
typedef struct mtk_shape mtk_shape;

MTK_API int32_t mtk_bridge_abi_version(void);

/* Version of the OpenCASCADE library the bridge was built against, e.g. "8.0.1". */
MTK_API const char* mtk_occt_version(void);

/* Message of the last error on the calling thread; valid until the next bridge call on the same thread. */
MTK_API const char* mtk_last_error_message(void);

MTK_API mtk_status mtk_shape_make_box(double dx, double dy, double dz, mtk_shape** out_shape);
MTK_API mtk_status mtk_shape_volume(const mtk_shape* shape, double* out_volume);
MTK_API void mtk_shape_release(mtk_shape* shape);

#ifdef __cplusplus
}
#endif

#endif /* MTK_OCCT_BRIDGE_H */
