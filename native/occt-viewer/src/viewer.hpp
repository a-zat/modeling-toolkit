#pragma once

#include "mtk/occt_viewer.h"
#include "scene.hpp"

#include <AIS_InteractiveContext.hxx>
#include <AIS_Line.hxx>
#include <AIS_ViewController.hxx>
#include <AIS_ViewCube.hxx>
#include <Aspect_GridParams.hxx>
#include <Aspect_NeutralWindow.hxx>
#include <NCollection_Vec2.hxx>
#include <V3d_View.hxx>
#include <V3d_Viewer.hxx>

#include <map>
#include <memory>
#include <optional>
#include <string>
#include <utility>
#include <vector>

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

    // The scene, from checked arguments: each function that adds returns the index of what it added.
    int add_mesh(const mtk_viewer_mesh& mesh);
    int add_texture(const uint8_t* pixels, int width, int height);
    int add_material(const mtk_viewer_material& material);
    int add_instance(int mesh, int material, const double* transform);
    void set_selection(const int32_t* instances, int count);
    void clear();
    void fit_all();

    // How the view shows the scene: the camera turns to a standard view with an animation, which redraw() plays.
    void show_view(mtk_viewer_view view);
    void set_display_mode(mtk_viewer_display_mode mode);
    void set_perspective(bool perspective);
    void set_grid_visible(bool visible);
    int mesh_count() const { return static_cast<int>(meshes_.size()); }
    int texture_count() const { return static_cast<int>(textures_.size()); }
    int material_count() const { return static_cast<int>(materials_.size()); }
    int instance_count() const { return static_cast<int>(instances_.size()); }

    // Input; each returns true when the view must be drawn again.
    bool mouse_buttons(double x, double y, uint32_t buttons, uint32_t modifiers);
    bool mouse_move(double x, double y, uint32_t buttons, uint32_t modifiers);
    bool mouse_leave();
    bool scroll(double x, double y, double delta);

    // Whether the view was clicked since the last call, and on which instance: -1 for none.
    bool take_pick(int& instance);

    // Applies the input and draws if needed; returns true while an animation needs more frames.
    bool redraw();

    const std::string& diagnostics();

private:
    void OnSelectionChanged(const occ::handle<AIS_InteractiveContext>& context,
                            const occ::handle<V3d_View>& view) override;
    void handleViewRedraw(const occ::handle<AIS_InteractiveContext>& context,
                          const occ::handle<V3d_View>& view) override;

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
    occ::handle<AIS_Line> vertical_axis_;
    Aspect_GridParams grid_;
    bool grid_visible_ = true;
    mtk_viewer_display_mode display_mode_ = MTK_VIEWER_DISPLAY_SHADED;
    Quantity_Color edge_color_ = Quantity_NOC_GRAY30;
    occ::handle<Aspect_NeutralWindow> window_;
    std::vector<occ::handle<Graphic3d_ArrayOfTriangles>> meshes_;
    std::vector<occ::handle<Graphic3d_Texture2D>> textures_;
    std::vector<MaterialAspects> materials_;
    std::map<std::pair<int, int>, occ::handle<MeshObject>> shared_meshes_; // by mesh and material
    std::vector<occ::handle<AIS_InteractiveObject>> instances_;
    Graphic3d_ZLayerId faded_layer_ = Graphic3d_ZLayerId_UNKNOWN;
    std::optional<int> pick_; // the instance clicked, -1 for none, not yet taken
    bool needs_next_frame_ = false; // whether the last redraw asked for another frame, see handleViewRedraw()
    double scale_ = 0.0;
    std::string failure_;
    std::string diagnostics_;
    std::unique_ptr<NativeView> native_view_;
};

} // namespace mtk
