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
#define MTK_OCCT_VIEWER_ABI_VERSION 3

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
    float cube_transparency; /* from 0, opaque, to 1 excluded */
    float highlight[3];      /* the part of the ViewCube under the pointer */
} mtk_viewer_colors;

/*
 * Creates a viewer and its native view: an NSView on macOS, a child window of parent (an HWND) on Windows. On macOS
 * parent is ignored, as the application adds the view to its window. The view is drawn once it is in a window and
 * has a size. It shows the ground, the XY plane, in perspective, with a grid of squares of one metre whose X and Y axes
 * cross at the origin: the unit of the scene is the millimetre, as in the documents.
 */
MTK_VIEWER_API mtk_status mtk_viewer_create(void* parent, mtk_viewer** out_viewer);

/* Releases the viewer and destroys its native view. */
MTK_VIEWER_API void mtk_viewer_release(mtk_viewer* viewer);

/* The native view, NSView* on macOS and HWND on Windows. It belongs to the viewer. */
MTK_VIEWER_API mtk_status mtk_viewer_get_native_view(const mtk_viewer* viewer, void** out_view);

MTK_VIEWER_API mtk_status mtk_viewer_set_colors(mtk_viewer* viewer, const mtk_viewer_colors* colors);

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
