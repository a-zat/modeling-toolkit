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

#include "mtk/status.h"

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
#define MTK_OCCT_BRIDGE_ABI_VERSION 4

/* Opaque handle to an OpenCASCADE shape. */
typedef struct mtk_shape mtk_shape;

MTK_API int32_t mtk_bridge_abi_version(void);

/* Version of the OpenCASCADE library the bridge was built against, e.g. "8.0.1". */
MTK_API const char* mtk_occt_version(void);

/* Message of the last error on the calling thread; valid until the next bridge call on the same thread. */
MTK_API const char* mtk_last_error_message(void);

/*
 * Analytic primitives, solids with their own axes: the box fills [0, dx] x [0, dy] x [0, dz]; the cylinder and the cone
 * stand on the XY plane, around the Z axis, from z = 0 to z = height; the sphere and the torus are centred at the
 * origin, the torus around the Z axis. The cone narrows (or widens) from bottom_radius to top_radius, which may be
 * zero for a point but not both, and must differ from bottom_radius: equal radii make a cylinder. The minor radius of
 * the torus must be smaller than its major radius.
 */
MTK_API mtk_status mtk_shape_make_box(double dx, double dy, double dz, mtk_shape** out_shape);
MTK_API mtk_status mtk_shape_make_cylinder(double radius, double height, mtk_shape** out_shape);
MTK_API mtk_status mtk_shape_make_sphere(double radius, mtk_shape** out_shape);
MTK_API mtk_status mtk_shape_make_cone(double bottom_radius, double top_radius, double height,
                                       mtk_shape** out_shape);
MTK_API mtk_status mtk_shape_make_torus(double major_radius, double minor_radius, mtk_shape** out_shape);

/*
 * A new shape: the shape moved by a placement, which shares its geometry. placement holds 12 values, the first three
 * rows of a 4x4 matrix applied to column vectors, row after row: a rotation and a translation, without scaling or
 * reflection.
 */
MTK_API mtk_status mtk_shape_place(const mtk_shape* shape, const double* placement, mtk_shape** out_shape);

MTK_API mtk_status mtk_shape_volume(const mtk_shape* shape, double* out_volume);

/* The smallest box with the sides along the axes that contains the shape: out_bounds receives the minimum x, y and z,
 * then the maximum x, y and z. */
MTK_API mtk_status mtk_shape_bounds(const mtk_shape* shape, double* out_bounds /* 6 values */);

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

/* The triangles of a shape: one mesh for all its faces, owned by the tessellation. */
typedef struct mtk_tessellation mtk_tessellation;

/*
 * Turns the faces of a shape into triangles. linear_deflection, in millimetres, is the largest distance allowed between
 * a triangle and the surface it stands for; angular_deflection, in radians, from 0 to pi, the largest angle between
 * the normals of neighbouring triangles. The shape is left as it was. A shape without faces is rejected.
 */
MTK_API mtk_status mtk_shape_tessellate(const mtk_shape* shape, double linear_deflection, double angular_deflection,
                                        mtk_tessellation** out_tessellation);

/*
 * The mesh, as for a scene, without a material or texture coordinates: the faces have their own vertices, with the
 * normals of their surfaces, so that edges between faces look sharp. The front of the triangles is on the outside of
 * the solid. The arrays are valid until mtk_tessellation_release.
 */
MTK_API mtk_status mtk_tessellation_get_mesh(const mtk_tessellation* tessellation, mtk_mesh_info* out_mesh);
MTK_API void mtk_tessellation_release(mtk_tessellation* tessellation);

#ifdef __cplusplus
}
#endif

#endif /* MTK_OCCT_BRIDGE_H */
