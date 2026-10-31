#pragma once

#include "mtk/occt_viewer.h"

#include <AIS_InteractiveContext.hxx>
#include <AIS_ViewController.hxx>
#include <AIS_ViewCube.hxx>
#include <Aspect_NeutralWindow.hxx>
#include <NCollection_Vec2.hxx>
#include <V3d_View.hxx>
#include <V3d_Viewer.hxx>

#include <memory>
#include <string>

namespace mtk {

class Viewer;

// The native view the viewer draws into, implemented for each platform: an NSView on macOS (native_view_cocoa.mm), a
// child window on Windows (native_view_win32.cpp). It tells the viewer when it changes and when it must be drawn, but
// not before create() returns: the viewer is still being constructed.
class NativeView {
public:
    static std::unique_ptr<NativeView> create(Viewer& viewer, void* parent);

    virtual ~NativeView() = default;

    // NSView* on macOS, HWND on Windows.
    virtual void* handle() const = 0;

    // In a window and with a size: OpenCASCADE can create its OpenGL context on it.
    virtual bool is_ready() const = 0;

    // Size of the surface in pixels.
    virtual NCollection_Vec2<int> pixel_size() const = 0;

    // Pixels per device-independent unit: 2 on a Retina display, 1.5 on Windows at 150%.
    virtual double scale() const = 0;

    // Stops telling the viewer, which is being destroyed.
    virtual void disconnect() = 0;
};

// An OpenCASCADE viewer with one view, navigated with the mouse (AIS_ViewController), with the ViewCube and the axes.
//
// OpenCASCADE works in pixels, like the OpenGL surface: the window given to the view has the size of the surface in
// pixels, and positions are converted from device-independent units. With the Cocoa window of OpenCASCADE, sized in
// points, the scene would still be picked correctly on a Retina display, but objects placed in pixels, like the
// ViewCube, would be drawn at half their size and picked where they would be at their full size.
class Viewer : public AIS_ViewController {
public:
    explicit Viewer(void* parent);
    ~Viewer() override;

    Viewer(const Viewer&) = delete;
    Viewer& operator=(const Viewer&) = delete;

    void* native_view() const { return native_view_->handle(); }

    // Called by the native view, from the event loop of the platform: they never throw. A failure stops the drawing
    // and is reported by the next redraw().
    void view_changed() noexcept; // entered a window, or changed size or pixel density
    void view_exposed() noexcept; // must be drawn again, after being covered for example

    void set_colors(const mtk_viewer_colors& colors);

    // Input; each returns true when the view must be drawn again.
    bool mouse_buttons(double x, double y, uint32_t buttons, uint32_t modifiers);
    bool mouse_move(double x, double y, uint32_t buttons, uint32_t modifiers);
    bool mouse_leave();
    bool scroll(double x, double y, double delta);

    // Applies the input and draws if needed; returns true while an animation needs more frames.
    bool redraw();

    const std::string& diagnostics();

private:
    bool is_attached() const { return !window_.IsNull(); }
    void attach();
    void update_size();
    void apply_scale(double scale);
    NCollection_Vec2<int> to_pixels(double x, double y) const;

    template <typename Body>
    void report_failures(Body&& body) noexcept;

    occ::handle<V3d_Viewer> viewer_;
    occ::handle<V3d_View> view_;
    occ::handle<AIS_InteractiveContext> context_;
    occ::handle<AIS_ViewCube> view_cube_;
    occ::handle<Aspect_NeutralWindow> window_;
    double scale_ = 0.0;
    std::string failure_;
    std::string diagnostics_;
    std::unique_ptr<NativeView> native_view_;
};

} // namespace mtk
