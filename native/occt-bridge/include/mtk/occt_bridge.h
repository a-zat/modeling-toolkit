/*
 * C API of the Modeling Toolkit bridge to OpenCASCADE.
 *
 * Conventions:
 *  - functions that can fail return an mtk_status; on failure mtk_last_error_message()
 *    describes the error and output parameters are set to zero/null;
 *  - objects created by the bridge are opaque handles owned by the caller,
 *    to be released with the matching *_release function;
 *  - lengths are in millimetres;
 *  - the functions on shapes may be called from several threads at the same time, on the same shape or on shapes that
 *    share geometry: they do not change the shapes they are given.
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
#define MTK_OCCT_BRIDGE_ABI_VERSION 8

/*
 * The progress of a long operation. report is called with the part done, from 0 to 1, as the operation goes, from the
 * thread of the call or from threads of the operation, but never twice at the same time; it returns nonzero to ask the
 * operation to stop, which it does as soon as it can, failing with MTK_STATUS_CANCELLED. The operations that take a
 * progress accept null for none.
 */
typedef int32_t (*mtk_progress_report)(double done, void* user_data);

typedef struct mtk_progress {
    mtk_progress_report report;
    void* user_data;
} mtk_progress;

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

typedef enum mtk_boolean_operation {
    MTK_BOOLEAN_UNION = 0,        /* what is in either shape */
    MTK_BOOLEAN_DIFFERENCE = 1,   /* what is in the first shape and not in the second */
    MTK_BOOLEAN_INTERSECTION = 2  /* what is in both shapes */
} mtk_boolean_operation;

/*
 * A new shape: the result of a boolean operation on two shapes, which are not changed. The faces and edges of the
 * result that lie on the same surface or curve are merged, so that, for example, the union of two boxes side by side
 * has six faces. A failure of the operation, for
 * example on shapes that are not valid, and an empty result, such as the intersection of shapes that do not meet, are
 * reported as MTK_STATUS_KERNEL_ERROR.
 */
MTK_API mtk_status mtk_shape_boolean(const mtk_shape* first, const mtk_shape* second,
                                     int32_t operation /* mtk_boolean_operation */, const mtk_progress* progress,
                                     mtk_shape** out_shape);

/* The smallest box with the sides along the axes that contains the shape: out_bounds receives the minimum x, y and z,
 * then the maximum x, y and z. */
MTK_API mtk_status mtk_shape_bounds(const mtk_shape* shape, double* out_bounds /* 6 values */);

MTK_API void mtk_shape_release(mtk_shape* shape);

/* Bytes owned by the bridge, such as the data of a shape written by mtk_shape_write. */
typedef struct mtk_bytes mtk_bytes;

/*
 * The shape in the BRep format of OpenCASCADE, a text that holds the geometry, the placements and the topology but no
 * triangles. The data is valid until mtk_bytes_release.
 */
MTK_API mtk_status mtk_shape_write(const mtk_shape* shape, mtk_bytes** out_bytes);
MTK_API mtk_status mtk_bytes_get(const mtk_bytes* bytes, const uint8_t** out_data, int64_t* out_size);
MTK_API void mtk_bytes_release(mtk_bytes* bytes);

/* A shape from data written by mtk_shape_write. Data that is not in the BRep format is rejected. */
MTK_API mtk_status mtk_shape_read(const uint8_t* data, int64_t size, mtk_shape** out_shape);

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
MTK_API mtk_status mtk_scene_read_gltf(const char* path, const mtk_progress* progress, mtk_scene** out_scene);
MTK_API void mtk_scene_release(mtk_scene* scene);

MTK_API mtk_status mtk_scene_get_info(const mtk_scene* scene, mtk_scene_info* out_info);
MTK_API mtk_status mtk_scene_get_block(const mtk_scene* scene, int32_t index, mtk_block_info* out_block);
MTK_API mtk_status mtk_scene_get_mesh(const mtk_scene* scene, int32_t index, mtk_mesh_info* out_mesh);
MTK_API mtk_status mtk_scene_get_reference(const mtk_scene* scene, int32_t index, mtk_reference_info* out_reference);
MTK_API mtk_status mtk_scene_get_material(const mtk_scene* scene, int32_t index, mtk_material_info* out_material);
MTK_API mtk_status mtk_scene_get_texture(const mtk_scene* scene, int32_t index, mtk_texture_info* out_texture);

/* Messages about data that could not be read and was skipped. */
MTK_API mtk_status mtk_scene_get_warning(const mtk_scene* scene, int32_t index, const char** out_message);

/* The triangles of a shape, one mesh for all its faces, and the polylines of its edges, owned by the tessellation. */
typedef struct mtk_tessellation mtk_tessellation;

/*
 * Turns the faces of a shape into triangles. linear_deflection, in millimetres, is the largest distance allowed between
 * a triangle and the surface it stands for; angular_deflection, in radians, from 0 to pi, the largest angle between
 * the normals of neighbouring triangles. The shape is left as it was. A shape without faces is rejected.
 */
MTK_API mtk_status mtk_shape_tessellate(const mtk_shape* shape, double linear_deflection, double angular_deflection,
                                        const mtk_progress* progress, mtk_tessellation** out_tessellation);

/*
 * The mesh, as for a scene, without a material or texture coordinates: the faces have their own vertices, with the
 * normals of their surfaces, so that edges between faces look sharp. The front of the triangles is on the outside of
 * the solid. The arrays are valid until mtk_tessellation_release.
 */
MTK_API mtk_status mtk_tessellation_get_mesh(const mtk_tessellation* tessellation, mtk_mesh_info* out_mesh);

/*
 * The triangles of each face of the shape, which follow one another in the mesh: face i has the triangles from
 * out_first_triangles[i] to out_first_triangles[i + 1] - 1. The array has out_face_count + 1 values, the last one being
 * the number of triangles, and is valid until mtk_tessellation_release. The faces come in the order of the shape.
 */
MTK_API mtk_status mtk_tessellation_get_faces(const mtk_tessellation* tessellation, const int32_t** out_first_triangles,
                                              int32_t* out_face_count);

/*
 * The edges of the shape as polylines that follow its curves within the deflection, through the vertices of the mesh
 * where they bound a face: edge i has the points from out_first_points[i] to out_first_points[i + 1] - 1, at least
 * two, of out_points (x, y, z per point). out_first_points has out_edge_count + 1 values, the last one being the number
 * of points. Each edge comes once, however many faces it bounds; seams, where a closed surface such as a cylinder
 * meets itself, and degenerate edges, such as the poles of a sphere, are left out. The arrays are valid until
 * mtk_tessellation_release.
 */
MTK_API mtk_status mtk_tessellation_get_edges(const mtk_tessellation* tessellation, const double** out_points,
                                              const int32_t** out_first_points, int32_t* out_edge_count);
MTK_API void mtk_tessellation_release(mtk_tessellation* tessellation);

#ifdef __cplusplus
}
#endif

#endif /* MTK_OCCT_BRIDGE_H */
