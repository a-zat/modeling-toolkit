#pragma once

#include "mtk/occt_viewer.h"
#include "overlay.hpp"
#include "scene.hpp"

#include <Graphic3d_CubeMap.hxx>
#include <AIS_InteractiveContext.hxx>
#include <AIS_Line.hxx>
#include <AIS_RubberBand.hxx>
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
    void set_instance(int instance, int mesh, int material, const double* transform);
    void remove_instance(int instance);
    void remove_mesh(int mesh);
    void set_selection(const int32_t* instances, int count);
    void clear();
    void fit_all();

    // How the view shows the scene: the camera turns to a standard view with an animation, which redraw() plays.
    void show_view(mtk_viewer_view view);
    void set_display_mode(mtk_viewer_display_mode mode);
    void set_render_style(mtk_viewer_render_style style);
    void set_perspective(bool perspective);
    void set_environment(const mtk_viewer_environment& environment);
    mtk_viewer_camera camera() const;
    void set_camera(const mtk_viewer_camera& camera);
    void set_grid_visible(bool visible);
    int mesh_count() const { return static_cast<int>(meshes_.size()); }
    int texture_count() const { return static_cast<int>(textures_.size()); }
    int material_count() const { return static_cast<int>(materials_.size()); }
    int instance_count() const { return static_cast<int>(instances_.size()); }

    // Whether an index refers to a mesh or an instance that was added and not removed.
    bool has_mesh(int mesh) const { return mesh >= 0 && mesh < mesh_count() && meshes_[mesh].present; }
    bool has_instance(int instance) const {
        return instance >= 0 && instance < instance_count() && !instances_[instance].IsNull();
    }
    // Whether an instance that was not removed places the mesh.
    bool is_placed(int mesh) const;

    // Input; each returns true when the view must be drawn again.
    bool mouse_buttons(double x, double y, uint32_t buttons, uint32_t modifiers);
    bool mouse_move(double x, double y, uint32_t buttons, uint32_t modifiers);
    bool mouse_leave();
    bool scroll(double x, double y, double delta);

    // Whether the view was clicked since the last call, and on what.
    bool take_pick(mtk_viewer_pick& pick);
    // What is under the pointer as of the last redraw; nothing on the ViewCube.
    mtk_viewer_pick hovered() const;

    // Shows an overlay in place of the previous one; an empty one shows none.
    void set_overlay(Overlay overlay);

    // Shows the rectangle of a window selection, between corners in device-independent units, or hides it.
    void set_window_band(bool shown, double x0, double y0, double x1, double y1, bool crossing);

    // Points of the model space where the camera shows them, in device-independent units; NaN behind the camera.
    void project(const double* points, int count, double* out_points) const;

    // Highlights checked elements as selected, in place of those highlighted before.
    void set_highlighted_elements(const mtk_viewer_element* elements, int count);

    // The owner of an element of the shape that an instance shows; null when it has no such element.
    occ::handle<ElementOwner> find_element(const mtk_viewer_element& element) const;

    // Applies the input and draws if needed; returns true while an animation needs more frames.
    bool redraw();

    const std::string& diagnostics();

private:
    void OnSelectionChanged(const occ::handle<AIS_InteractiveContext>& context,
                            const occ::handle<V3d_View>& view) override;
    void handleViewRedraw(const occ::handle<AIS_InteractiveContext>& context,
                          const occ::handle<V3d_View>& view) override;

    std::optional<mtk_viewer_pick> detected_pick() const;
    // A pick of nothing, with the ray of the pointer.
    mtk_viewer_pick nothing_picked() const;
    // The edges that the display mode and the render style draw.
    EdgeStyle edge_style() const;
    void apply_lights();
    void apply_environment(); // of the rendered display mode, or what the other modes show instead
    bool is_attached() const { return !window_.IsNull(); }
    void attach();
    void update_size();
    void apply_scale(double scale);
    void set_grid_step(double step);
    // Stops highlighting the elements of an instance, which changes or goes.
    void forget_highlighted(int instance);
    occ::handle<AIS_InteractiveObject> make_instance(int mesh, int material, const double* transform);
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
    // The rendered display mode: its environment, the ground shown under the model, the colours of the background of
    // the other modes, and the shading of the view in those modes.
    mtk_viewer_environment environment_{{0.3, -0.5, 0.8}, 0.3f, 0.0, 100000.0};
    occ::handle<AIS_InteractiveObject> ground_;
    occ::handle<Graphic3d_CubeMap> sky_; // painted for the sun and the clouds of sky_of_
    mtk_viewer_environment sky_of_{};
    Quantity_Color background_top_, background_bottom_;
    double grid_step_ = 1000.0; // millimetres
    mtk_viewer_display_mode display_mode_ = MTK_VIEWER_DISPLAY_SHADED;
    mtk_viewer_render_style render_style_ = MTK_VIEWER_RENDER_STANDARD;
    Quantity_Color edge_color_ = Quantity_NOC_GRAY30;
    Quantity_Color outline_color_ = Quantity_NOC_BLACK;
    occ::handle<Aspect_NeutralWindow> window_;
    std::vector<Mesh> meshes_;
    EdgeAspects edge_aspects_ = make_edge_aspects();
    std::vector<occ::handle<Graphic3d_Texture2D>> textures_;
    std::vector<MaterialAspects> materials_;
    std::map<std::pair<int, int>, occ::handle<MeshObject>> shared_meshes_; // by mesh and material
    // The removed meshes have no triangles, the removed instances are null: indices are never given again.
    std::vector<occ::handle<AIS_InteractiveObject>> instances_;
    std::vector<int> instance_meshes_; // the mesh of each instance
    Graphic3d_ZLayerId faded_layer_ = Graphic3d_ZLayerId_UNKNOWN;
    std::optional<mtk_viewer_pick> pick_; // what was clicked, not yet taken
    std::vector<std::pair<int, occ::handle<ElementOwner>>> highlighted_; // by instance
    Overlay overlay_;
    occ::handle<OverlayObject> overlay_object_; // null when the overlay is empty
    occ::handle<AIS_RubberBand> window_band_; // null until a window selection is shown
    bool needs_next_frame_ = false; // whether the last redraw asked for another frame, see handleViewRedraw()
    double scale_ = 0.0;
    std::string failure_;
    std::string diagnostics_;
    std::unique_ptr<NativeView> native_view_;
};

} // namespace mtk
