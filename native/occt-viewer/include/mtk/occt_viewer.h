/*
 * C API of the Modeling Toolkit viewer: an OpenCASCADE view drawn into a native view, which the application hosts in
 * its window (NativeControlHost in Avalonia).
 *
 * Conventions, as for the bridge (occt_bridge.h):
 *  - functions that can fail return an mtk_status; on failure mtk_viewer_last_error_message() describes the error
 *    and output parameters are set to zero/null;
 *  - a viewer is an opaque handle owned by the caller, to be released with mtk_viewer_release;
 *  - every function must be called on the thread of the user interface, which owns the native view;
 *  - flags returned through int32_t pointers are 1 or 0.
 *
 * Positions are in device-independent units from the top-left corner of the view, as the user interface reports
 * them: points on macOS, 1/96 of an inch on Windows. The viewer converts them into pixels.
 */
#ifndef MTK_OCCT_VIEWER_H
#define MTK_OCCT_VIEWER_H

#include "mtk/status.h"

#include <stdint.h>

#if defined(_WIN32)
#  if defined(MTK_OCCT_VIEWER_EXPORTS)
#    define MTK_VIEWER_API __declspec(dllexport)
#  else
#    define MTK_VIEWER_API __declspec(dllimport)
#  endif
#else
#  define MTK_VIEWER_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Incremented on every change of this API; the managed side checks it when loading the library. */
#define MTK_OCCT_VIEWER_ABI_VERSION 9

MTK_VIEWER_API int32_t mtk_viewer_abi_version(void);

/* Message of the last error on the calling thread; valid until the next viewer call on the same thread. */
MTK_VIEWER_API const char* mtk_viewer_last_error_message(void);

/* Opaque handle to a viewer: an OpenCASCADE viewer with one view, and the native view it draws into. */
typedef struct mtk_viewer mtk_viewer;

/* Mouse buttons, combined as bit flags. */
typedef enum mtk_mouse_button {
    MTK_MOUSE_BUTTON_LEFT = 1,
    MTK_MOUSE_BUTTON_MIDDLE = 2,
    MTK_MOUSE_BUTTON_RIGHT = 4
} mtk_mouse_button;

/* Modifier keys, combined as bit flags. */
typedef enum mtk_modifier_key {
    MTK_MODIFIER_KEY_SHIFT = 1,
    MTK_MODIFIER_KEY_CONTROL = 2,
    MTK_MODIFIER_KEY_ALT = 4, /* Option on macOS */
    MTK_MODIFIER_KEY_META = 8 /* Command on macOS, Windows key on Windows */
} mtk_modifier_key;

/* Colours of the view and of its navigation aids, as sRGB components from 0 to 1. */
typedef struct mtk_viewer_colors {
    float background_top[3];
    float background_bottom[3];
    float grid[3];   /* the lines of the grid on the ground */
    float axis_x[3]; /* the axes in the lower left corner, with their labels */
    float axis_y[3];
    float axis_z[3];
    float cube_face[3]; /* the ViewCube in the upper right corner */
    float cube_edge[3]; /* its bevelled edges and corners */
    float cube_text[3];
    float highlight[3]; /* the part of the ViewCube under the pointer */
    float edges[3];     /* the edges, in the display modes that draw them */
} mtk_viewer_colors;

/* The views of the faces of the ViewCube, and the view from its front right top corner, the initial one. */
typedef enum mtk_viewer_view {
    MTK_VIEWER_VIEW_TOP = 0,
    MTK_VIEWER_VIEW_BOTTOM = 1,
    MTK_VIEWER_VIEW_FRONT = 2,
    MTK_VIEWER_VIEW_BACK = 3,
    MTK_VIEWER_VIEW_LEFT = 4,
    MTK_VIEWER_VIEW_RIGHT = 5,
    MTK_VIEWER_VIEW_ISOMETRIC = 6
} mtk_viewer_view;

/* How the meshes are drawn. */
typedef enum mtk_viewer_display_mode {
    MTK_VIEWER_DISPLAY_SHADED = 0,
    MTK_VIEWER_DISPLAY_SHADED_WITH_EDGES = 1, /* with the edges of the meshes */
    MTK_VIEWER_DISPLAY_WIREFRAME = 2          /* only the edges of the meshes */
} mtk_viewer_display_mode;

/*
 * Creates a viewer and its native view: an NSView on macOS, a child window of parent (an HWND) on Windows. On macOS
 * parent is ignored, as the application adds the view to its window. The view is drawn once it is in a window and
 * has a size. It shows the ground, the XY plane, in perspective, with a grid of squares of one metre and the X, Y and Z
 * axes through the origin: the unit of the scene is the millimetre, as in the documents.
 */
MTK_VIEWER_API mtk_status mtk_viewer_create(void* parent, mtk_viewer** out_viewer);

/* Releases the viewer and destroys its native view. */
MTK_VIEWER_API void mtk_viewer_release(mtk_viewer* viewer);

/* The native view, NSView* on macOS and HWND on Windows. It belongs to the viewer. */
MTK_VIEWER_API mtk_status mtk_viewer_get_native_view(const mtk_viewer* viewer, void** out_view);

MTK_VIEWER_API mtk_status mtk_viewer_set_colors(mtk_viewer* viewer, const mtk_viewer_colors* colors);

/*
 * The scene: meshes, each uploaded once, placed by instances with a material. The functions below return indices,
 * from 0, that identify meshes, textures, materials and instances until mtk_viewer_clear. After changing the scene,
 * the view must be drawn again with mtk_viewer_redraw. Arrays are read during the call only.
 */

/*
 * A triangle mesh, and the edges that the display modes with edges draw: those of its triangles, or polylines of its
 * own, such as the edges of the solid it is the tessellation of.
 */
typedef struct mtk_viewer_mesh {
    const double* positions; /* 3 per vertex, in millimetres */
    const float* normals;    /* 3 per vertex, of unit length; null for flat shading */
    const float* uvs;        /* 2 per vertex, (0, 0) at the bottom left corner of the textures; or null */
    int32_t vertex_count;
    const int32_t* triangles; /* 3 indices of vertices per triangle, counter-clockwise seen from its front */
    int32_t triangle_count;
    /* Edges of its own: null edge_first_points draws the edges of the triangles. Otherwise edge_first_points has
     * edge_count + 1 values, and edge i the points from edge_first_points[i] to edge_first_points[i + 1] - 1, two or
     * more, of edge_points (3 values per point, in millimetres), which may be null when there are no edges. */
    const double* edge_points;
    const int32_t* edge_first_points;
    int32_t edge_count;
} mtk_viewer_mesh;

/* A material of the metallic-roughness model of glTF. */
typedef struct mtk_viewer_material {
    float base_color[4];        /* linear RGB, then the opacity */
    float metallic;             /* from 0, dielectric, to 1, metal */
    float roughness;            /* from 0, smooth, to 1, rough */
    int32_t base_color_texture; /* multiplied by base_color; -1 for none */
} mtk_viewer_material;

MTK_VIEWER_API mtk_status mtk_viewer_add_mesh(mtk_viewer* viewer, const mtk_viewer_mesh* mesh, int32_t* out_mesh);

/* An image of width x height pixels of 4 bytes, RGBA in sRGB, row after row from the top one. */
MTK_VIEWER_API mtk_status mtk_viewer_add_texture(mtk_viewer* viewer, const uint8_t* pixels, int32_t width,
                                                 int32_t height, int32_t* out_texture);

MTK_VIEWER_API mtk_status mtk_viewer_add_material(mtk_viewer* viewer, const mtk_viewer_material* material,
                                                  int32_t* out_material);

/*
 * Places a mesh with a material. transform holds 12 values: the first three rows of a 4x4 matrix applied to column
 * vectors, row after row, from the coordinates of the mesh to those of the scene.
 */
MTK_VIEWER_API mtk_status mtk_viewer_add_instance(mtk_viewer* viewer, int32_t mesh, int32_t material,
                                                  const double* transform, int32_t* out_instance);

/*
 * Shows count instances as selected, in place of the previous selection: the others are faded and drawn behind them,
 * so that the selection stands out. With none selected, or all, none is faded. instances may be null when count is 0.
 */
MTK_VIEWER_API mtk_status mtk_viewer_set_selection(mtk_viewer* viewer, const int32_t* instances, int32_t count);

/* Removes the meshes, textures, materials and instances. */
MTK_VIEWER_API mtk_status mtk_viewer_clear(mtk_viewer* viewer);

/* Turns the camera to show all the instances; with none, the view does not change. */
MTK_VIEWER_API mtk_status mtk_viewer_fit_all(mtk_viewer* viewer);

/* Turns the camera to a standard view, fitting the instances, with the animation of the ViewCube. */
MTK_VIEWER_API mtk_status mtk_viewer_show_view(mtk_viewer* viewer, int32_t view /* mtk_viewer_view */);

/* Shaded is the initial mode. */
MTK_VIEWER_API mtk_status mtk_viewer_set_display_mode(mtk_viewer* viewer, int32_t mode /* mtk_viewer_display_mode */);

/* Perspective (1), the initial projection, or orthographic (0). */
MTK_VIEWER_API mtk_status mtk_viewer_set_perspective(mtk_viewer* viewer, int32_t perspective);

/* Shows (1), as initially, or hides (0) the grid on the ground and the axes through the origin. */
MTK_VIEWER_API mtk_status mtk_viewer_set_grid_visible(mtk_viewer* viewer, int32_t visible);

/*
 * Mouse input, with the buttons pressed after the event and the modifier keys held. Each function sets out_redraw
 * when the view changes: mtk_viewer_redraw should then be called, once, on the next frame of the user interface.
 */
MTK_VIEWER_API mtk_status mtk_viewer_mouse_buttons(mtk_viewer* viewer, double x, double y, uint32_t buttons,
                                                   uint32_t modifiers, int32_t* out_redraw);
MTK_VIEWER_API mtk_status mtk_viewer_mouse_move(mtk_viewer* viewer, double x, double y, uint32_t buttons,
                                                uint32_t modifiers, int32_t* out_redraw);

/* The pointer left the view: the object under it is no longer highlighted. */
MTK_VIEWER_API mtk_status mtk_viewer_mouse_leave(mtk_viewer* viewer, int32_t* out_redraw);

/*
 * Whether the view was clicked since the last call, other than on the ViewCube, which turns the view instead: clicks
 * are applied by mtk_viewer_redraw. out_instance receives the instance under the pointer, or -1 for none. The viewer
 * shows no selection by itself: see mtk_viewer_set_selection.
 */
MTK_VIEWER_API mtk_status mtk_viewer_take_pick(mtk_viewer* viewer, int32_t* out_picked, int32_t* out_instance);

/* Zooms at (x, y) by delta notches of the mouse wheel, positive to zoom in; fractions come from touchpads. */
MTK_VIEWER_API mtk_status mtk_viewer_scroll(mtk_viewer* viewer, double x, double y, double delta,
                                            int32_t* out_redraw);

/*
 * Applies the input received since the last call and draws the view if it changed. out_animating is set while an
 * animation, of the camera for example, needs more frames: the function should be called again on the next one.
 * Fails if the view could not be shown in its window, for example without a suitable OpenGL driver.
 */
MTK_VIEWER_API mtk_status mtk_viewer_redraw(mtk_viewer* viewer, int32_t* out_animating);

/*
 * Description of the view and of the OpenGL driver, one "name: value" per line, for the logs. The text belongs to
 * the viewer and stays valid until the next call.
 */
MTK_VIEWER_API mtk_status mtk_viewer_get_diagnostics(mtk_viewer* viewer, const char** out_text);

#ifdef __cplusplus
}
#endif

#endif /* MTK_OCCT_VIEWER_H */
