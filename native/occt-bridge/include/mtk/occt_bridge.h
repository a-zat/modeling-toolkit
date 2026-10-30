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

/* Incremented on every change of this API; the managed side checks it when loading the library. */
#define MTK_OCCT_BRIDGE_ABI_VERSION 2

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

/*
 * Scenes: models read from files, as tables of blocks, meshes, references, materials and textures.
 * Block 0 is the model space; the other blocks are placed through references. Coordinates are in millimetres,
 * Z up. Strings are UTF-8. Every pointer returned by the mtk_scene_get_* functions belongs to the scene and stays
 * valid until mtk_scene_release.
 */
typedef struct mtk_scene mtk_scene;

typedef struct mtk_scene_info {
    int32_t block_count;
    int32_t mesh_count;
    int32_t reference_count;
    int32_t material_count;
    int32_t texture_count;
    int32_t warning_count;
} mtk_scene_info;

typedef struct mtk_block_info {
    const char* name;
    int32_t first_mesh; /* the meshes of a block are consecutive */
    int32_t mesh_count;
    int32_t first_reference; /* the references contained in a block are consecutive */
    int32_t reference_count;
} mtk_block_info;

typedef struct mtk_mesh_info {
    int32_t material; /* index of the material, or -1 */
    int32_t vertex_count;
    int32_t triangle_count;
    const double* vertices;            /* x, y, z per vertex */
    const float* normals;              /* x, y, z per vertex, or null */
    const float* texture_coordinates;  /* u, v per vertex, or null; (0, 0) is the bottom-left corner of the image */
    const int32_t* triangles;          /* three vertex indices per triangle, counter-clockwise seen from the front */
} mtk_mesh_info;

typedef struct mtk_reference_info {
    const char* name;
    int32_t block;        /* index of the referenced block */
    double transform[12]; /* rows of a 3x4 affine matrix, from the referenced block to the containing one */
} mtk_reference_info;

typedef struct mtk_material_info {
    const char* name;
    float base_color[4]; /* linear RGB and opacity */
    float metallic;
    float roughness;
    int32_t base_color_texture; /* index of the texture, or -1 */
} mtk_material_info;

typedef struct mtk_texture_info {
    const char* name;
    const char* media_type; /* e.g. "image/png" */
    const uint8_t* data;    /* encoded image */
    int64_t size;
} mtk_texture_info;

/* Reads a glTF (.gltf or .glb) file, converting it from metres and Y up. */
MTK_API mtk_status mtk_scene_read_gltf(const char* path, mtk_scene** out_scene);
MTK_API void mtk_scene_release(mtk_scene* scene);

MTK_API mtk_status mtk_scene_get_info(const mtk_scene* scene, mtk_scene_info* out_info);
MTK_API mtk_status mtk_scene_get_block(const mtk_scene* scene, int32_t index, mtk_block_info* out_block);
MTK_API mtk_status mtk_scene_get_mesh(const mtk_scene* scene, int32_t index, mtk_mesh_info* out_mesh);
MTK_API mtk_status mtk_scene_get_reference(const mtk_scene* scene, int32_t index, mtk_reference_info* out_reference);
MTK_API mtk_status mtk_scene_get_material(const mtk_scene* scene, int32_t index, mtk_material_info* out_material);
MTK_API mtk_status mtk_scene_get_texture(const mtk_scene* scene, int32_t index, mtk_texture_info* out_texture);

/* Messages about data that could not be read and was skipped. */
MTK_API mtk_status mtk_scene_get_warning(const mtk_scene* scene, int32_t index, const char** out_message);

#ifdef __cplusplus
}
#endif

#endif /* MTK_OCCT_BRIDGE_H */
