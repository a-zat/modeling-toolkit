/*
 * Status codes returned by the C APIs of the Modeling Toolkit native libraries.
 */
#ifndef MTK_STATUS_H
#define MTK_STATUS_H

typedef enum mtk_status {
    MTK_STATUS_OK = 0,
    MTK_STATUS_INVALID_ARGUMENT = 1, /* the caller passed an invalid value */
    MTK_STATUS_KERNEL_ERROR = 2,     /* OpenCASCADE raised an exception */
    MTK_STATUS_INTERNAL_ERROR = 3,   /* any other unexpected failure */
    MTK_STATUS_CANCELLED = 4         /* the caller asked a long operation to stop, through its progress */
} mtk_status;

#endif /* MTK_STATUS_H */
