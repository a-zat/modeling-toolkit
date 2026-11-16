#include "viewer.hpp"

#include "mtk/errors.hpp"
#include "sky.hpp"

#include <AIS_AnimationCamera.hxx>
#include <AIS_ConnectedInteractive.hxx>
#include <AIS_Line.hxx>
#include <Graphic3d_ArrayOfTriangles.hxx>
#include <Graphic3d_AspectFillArea3d.hxx>
#include <Graphic3d_Group.hxx>
#include <Prs3d_Presentation.hxx>
#include <Aspect_DisplayConnection.hxx>
#include <Aspect_GridParams.hxx>
#include <Aspect_ScrollDelta.hxx>
#include <Bnd_Box.hxx>
#include <Graphic3d_Camera.hxx>
#include <Graphic3d_DiagnosticInfo.hxx>
#include <Graphic3d_TransformPers.hxx>
#include <Geom_Line.hxx>
#include <NCollection_IndexedDataMap.hxx>
#include <OpenGl_GraphicDriver.hxx>
#include <Prs3d_Drawer.hxx>
#include <Prs3d_LineAspect.hxx>
#include <Prs3d_ShadingAspect.hxx>
#include <Prs3d_TextAspect.hxx>
#include <SelectMgr_Selection.hxx>
#include <SelectMgr_SensitiveEntity.hxx>
#include <SelectMgr_SortCriterion.hxx>
#include <StdSelect_ViewerSelector3d.hxx>
#include <TCollection_AsciiString.hxx>
#include <V3d_AmbientLight.hxx>
#include <V3d_DirectionalLight.hxx>
#include <V3d_Trihedron.hxx>

#include <algorithm>
#include <cmath>
#include <limits>
#include <sstream>

// Definition of the opaque handle declared in the public header.
struct mtk_viewer : mtk::Viewer {
    using mtk::Viewer::Viewer;
};

// The managed side declares the same layouts: these sizes must match the ones it checks in its tests.
static_assert(sizeof(mtk_viewer_colors) == 144);
static_assert(sizeof(mtk_viewer_material) == 28);
static_assert(sizeof(void*) != 8 || sizeof(mtk_viewer_mesh) == 120);
static_assert(sizeof(mtk_viewer_element) == 12);
static_assert(sizeof(mtk_viewer_pick) == 120);
static_assert(sizeof(void*) != 8 || sizeof(mtk_viewer_overlay_line) == 32);
static_assert(sizeof(mtk_viewer_overlay_marker) == 40);
static_assert(sizeof(void*) != 8 || sizeof(mtk_viewer_overlay_label) == 56);
static_assert(sizeof(void*) != 8 || sizeof(mtk_viewer_overlay) == 48);

namespace {

// Sizes in device-independent units, like the rest of the interface; the viewer multiplies them by the scale.
// The edges highlighted, hovered or selected, in device-independent units.
constexpr double highlighted_edge_width = 3.0;
constexpr double view_cube_size = 42.0;
constexpr double view_cube_offset = 70.0; // from the upper right corner to the centre of the cube
constexpr double view_cube_bevel = 0.08;  // width of the edges and corners, relative to the size
constexpr double view_cube_text = 0.13;   // height of the labels, relative to the size
constexpr double axes_size = 0.07;        // unit of V3d_Trihedron: 500 pixels
constexpr double axes_label_height = 12.0;

// The camera and the ground, in millimetres.
constexpr double field_of_view = 35.0;         // vertical, in degrees
constexpr double initial_view_size = 12000.0; // the ground shown before a model is opened
constexpr double default_grid_step = 1000.0; // before a model is opened
constexpr double grid_squares = 5.0;          // at least this many squares across the diagonal of a model
constexpr double min_grid_step = 0.01;
constexpr double fit_margin = 0.1; // around the scene, relative to its size on screen

// The instance under the pointer is tinted with the colour of the highlight, half transparent.
constexpr float hover_transparency = 0.5f;

// How far from an edge or a vertex the pointer picks it, in device-independent units.
constexpr double pick_tolerance = 5.0;

// Textures larger than graphics cards accept, and meshes larger than the arrays of OpenCASCADE can index with three
// indices or vertices per triangle, are refused.
constexpr int32_t max_texture_size = 16384;
constexpr int32_t max_mesh_size = std::numeric_limits<int32_t>::max() / 3;

// The sans-serif font of the system: OpenCASCADE would draw the labels of the axes in Courier.
#if defined(__APPLE__)
constexpr const char* label_font = "Helvetica Neue";
#else
constexpr const char* label_font = "Segoe UI";
#endif

Aspect_VKeyMouse to_occt_buttons(uint32_t buttons) {
    Aspect_VKeyMouse result = Aspect_VKeyMouse_NONE;
    if ((buttons & MTK_MOUSE_BUTTON_LEFT) != 0) {
        result |= Aspect_VKeyMouse_LeftButton;
    }
    if ((buttons & MTK_MOUSE_BUTTON_MIDDLE) != 0) {
        result |= Aspect_VKeyMouse_MiddleButton;
    }
    if ((buttons & MTK_MOUSE_BUTTON_RIGHT) != 0) {
        result |= Aspect_VKeyMouse_RightButton;
    }
    return result;
}

Aspect_VKeyFlags to_occt_modifiers(uint32_t modifiers) {
    Aspect_VKeyFlags result = Aspect_VKeyFlags_NONE;
    if ((modifiers & MTK_MODIFIER_KEY_SHIFT) != 0) {
        result |= Aspect_VKeyFlags_SHIFT;
    }
    if ((modifiers & MTK_MODIFIER_KEY_CONTROL) != 0) {
        result |= Aspect_VKeyFlags_CTRL;
    }
    if ((modifiers & MTK_MODIFIER_KEY_ALT) != 0) {
        result |= Aspect_VKeyFlags_ALT;
    }
    if ((modifiers & MTK_MODIFIER_KEY_META) != 0) {
        result |= Aspect_VKeyFlags_META;
    }
    return result;
}

bool is_color(const float (&rgb)[3]) {
    for (float component : rgb) {
        if (!std::isfinite(component) || component < 0.0f || component > 1.0f) {
            return false;
        }
    }
    return true;
}

Quantity_Color to_color(const float (&rgb)[3]) {
    return Quantity_Color(rgb[0], rgb[1], rgb[2], Quantity_TOC_sRGB);
}

// The ViewCube and the axes keep a parallel projection in a view in perspective: in its corners they would be
// distorted.
Graphic3d_TransModeFlags navigation_aid_persistence() {
    return Graphic3d_TMF_TriedronPers | Graphic3d_TMF_OrthoPers;
}

// The grid on the ground, drawn by a shader over the whole plane, with its X and Y axes in red and green: they cross at
// the origin. Its lines are hidden by the objects in front of them.
Aspect_GridParams ground_grid(const Quantity_Color& color, double step) {
    Aspect_GridParams grid;
    grid.SetColor(color);
    grid.SetScale(1.0 / step); // squares per millimetre
    grid.SetIsDrawAxis(true);
    return grid;
}

// The step of the grid for a model of this diagonal: a power of ten, with some squares across the model.
double grid_step_for(double diagonal) {
    if (!std::isfinite(diagonal) || diagonal <= 0.0) {
        return default_grid_step;
    }
    return std::max(std::pow(10.0, std::floor(std::log10(diagonal / grid_squares))), min_grid_step);
}

} // namespace

template <typename Body>
void mtk::Viewer::report_failures(Body&& body) noexcept {
    try {
        body();
    } catch (const Standard_Failure& failure) {
        failure_ = std::string(failure.ExceptionType()) + ": " + failure.what();
    } catch (const std::exception& exception) {
        failure_ = exception.what();
    } catch (...) {
        failure_ = "Unknown native exception.";
    }
}

mtk::Viewer::Viewer(void* parent) {
    occ::handle<OpenGl_GraphicDriver> driver = new OpenGl_GraphicDriver(new Aspect_DisplayConnection(), false);
#if defined(__APPLE__)
    // The compatible profile is OpenGL 2.1 on macOS, without multisampling: the core profile is OpenGL 4.1.
    driver->ChangeOptions().contextCompatible = false;
#endif

    viewer_ = new V3d_Viewer(driver);
    viewer_->SetDefaultLights();
    viewer_->SetLightOn();

    // Around a selection, the other instances are faded and drawn first, in a layer of their own; the default layer
    // then clears the depth, so that the selection is drawn whole over them. The grid, drawn after both, is hidden only
    // by what is not faded.
    Graphic3d_ZLayerSettings faded_layer;
    faded_layer.SetName("Faded");
    faded_layer.SetClearDepth(false);
    viewer_->InsertLayerBefore(faded_layer_, faded_layer, Graphic3d_ZLayerId_Default);
    Graphic3d_ZLayerSettings default_layer = viewer_->ZLayerSettings(Graphic3d_ZLayerId_Default);
    default_layer.SetClearDepth(true);
    viewer_->SetZLayerSettings(Graphic3d_ZLayerId_Default, default_layer);

    context_ = new AIS_InteractiveContext(viewer_);

    // The vertical axis through the origin, which the grid on the ground does not draw, in a blue that goes with its red
    // and green axes. Shown, never selected.
    vertical_axis_ = new AIS_Line(new Geom_Line(gp::OZ()));
    vertical_axis_->SetColor(Quantity_Color(0.15, 0.35, 1.0, Quantity_TOC_sRGB));
    vertical_axis_->SetWidth(1.0);
    context_->Display(vertical_axis_, 0, -1, false);

    view_ = viewer_->CreateView();
    // Drawn only by redraw() and by the native view, never as a side effect of a change.
    view_->SetImmediateUpdate(false);
    view_->SetProj(V3d_TypeOfOrientation_Zup_AxoRight);
    view_->Camera()->SetProjectionType(Graphic3d_Camera::Projection_Perspective);
    view_->Camera()->SetFOVy(field_of_view);
    view_->ChangeRenderingParams().NbMsaaSamples = 4;
    grid_ = ground_grid(Quantity_NOC_GRAY70, grid_step_);
    view_->GridDisplay(grid_);

    // In the lower left corner, at a distance set by apply_scale().
    const occ::handle<V3d_Trihedron>& axes = view_->Trihedron();
    axes->TransformPersistence()->SetPersistence(navigation_aid_persistence(), Aspect_TOTP_LEFT_LOWER,
                                                 NCollection_Vec2<int>(0, 0));
    for (V3d_TypeOfAxe axis : {V3d_X, V3d_Y, V3d_Z}) {
        axes->LabelAspect(axis)->SetFont(label_font);
        axes->LabelAspect(axis)->SetHeight(axes_label_height);
    }

    // Clicking a face of the cube animates the camera to that view, frame after frame (see redraw()).
    view_cube_ = new AIS_ViewCube();
    view_cube_->SetDrawAxes(false); // the axes are in the lower left corner
    view_cube_->SetFont(label_font);
    view_cube_->SetViewAnimation(myViewAnimation);
    view_cube_->SetFixedAnimationLoop(false);
    view_cube_->SetAutoStartAnimation(true);
    apply_scale(1.0);
    context_->Display(view_cube_, false);

    native_view_ = NativeView::create(*this, parent);
}

mtk::Viewer::~Viewer() {
    native_view_->disconnect();
    try {
        // OpenCASCADE must release its OpenGL context before the native view is destroyed, after this body. The
        // animation of the ViewCube keeps the view too.
        myViewAnimation->SetView(occ::handle<V3d_View>());
        view_->Remove();
        view_cube_.Nullify();
        context_.Nullify();
        view_.Nullify();
        viewer_.Nullify();
        window_.Nullify();
    } catch (...) {
        // Nothing can be reported from a destructor.
    }
}

void mtk::Viewer::view_changed() noexcept {
    report_failures([&] {
        if (!is_attached()) {
            attach();
        } else if (native_view_->is_ready()) {
            update_size();
            view_->Invalidate();
            FlushViewEvents(context_, view_, true);
        }
    });
}

void mtk::Viewer::view_exposed() noexcept {
    report_failures([&] {
        if (is_attached()) {
            view_->Invalidate();
            FlushViewEvents(context_, view_, true);
        }
    });
}

void mtk::Viewer::set_colors(const mtk_viewer_colors& colors) {
    background_top_ = to_color(colors.background_top);
    background_bottom_ = to_color(colors.background_bottom);
    grid_ = ground_grid(to_color(colors.grid), grid_step_);
    if (display_mode_ != MTK_VIEWER_DISPLAY_RENDERED) {
        view_->SetBgGradientColors(background_top_, background_bottom_, Aspect_GradientFillMethod_Vertical);
        if (grid_visible_) {
            view_->GridDisplay(grid_);
        }
    }

    const occ::handle<V3d_Trihedron>& axes = view_->Trihedron();
    const Quantity_Color x = to_color(colors.axis_x), y = to_color(colors.axis_y), z = to_color(colors.axis_z);
    axes->SetArrowsColor(x, y, z);
    axes->SetLabelsColor(x, y, z);

    view_cube_->SetBoxColor(to_color(colors.cube_face));
    view_cube_->BoxEdgeStyle()->SetColor(to_color(colors.cube_edge));
    view_cube_->BoxCornerStyle()->SetColor(to_color(colors.cube_edge));
    view_cube_->SetTextColor(to_color(colors.cube_text));
    const Quantity_Color highlight = to_color(colors.highlight);
    view_cube_->DynamicHilightAttributes()->SetColor(highlight);
    view_cube_->DynamicHilightAttributes()->ShadingAspect()->SetColor(highlight);
    context_->Redisplay(view_cube_, false);
    const occ::handle<Prs3d_Drawer>& hover = context_->HighlightStyle(Prs3d_TypeOfHighlight_Dynamic);
    hover->SetColor(highlight);
    hover->SetTransparency(hover_transparency);
    context_->HighlightStyle(Prs3d_TypeOfHighlight_Selected)->SetColor(highlight);
    // The selected elements take the new colour.
    for (const auto& [instance, owner] : highlighted_) {
        owner->highlight_selected(context_->MainPrsMgr(), context_->HighlightStyle(Prs3d_TypeOfHighlight_Selected));
    }

    edge_color_ = to_color(colors.edges);
    outline_color_ = to_color(colors.outlines);
    set_display_mode(display_mode_);
    view_->Invalidate();
}

int mtk::Viewer::add_mesh(const mtk_viewer_mesh& mesh) {
    meshes_.push_back(make_mesh(mesh));
    return mesh_count() - 1;
}

int mtk::Viewer::add_texture(const uint8_t* pixels, int width, int height) {
    textures_.push_back(make_texture(pixels, width, height));
    return texture_count() - 1;
}

int mtk::Viewer::add_material(const mtk_viewer_material& material) {
    const occ::handle<Graphic3d_Texture2D> texture =
        material.base_color_texture >= 0 ? textures_[material.base_color_texture] : nullptr;
    MaterialAspects aspects = make_aspects(material, texture, render_style_);
    apply_display_mode(aspects, display_mode_, edge_style());
    materials_.push_back(std::move(aspects));
    return material_count() - 1;
}

occ::handle<AIS_InteractiveObject> mtk::Viewer::make_instance(int mesh, int material, const double* transform) {
    occ::handle<AIS_InteractiveObject> instance;
    if (const std::optional<gp_Trsf> placement = to_placement(transform)) {
        occ::handle<MeshObject>& shared = shared_meshes_[{mesh, material}];
        if (shared.IsNull()) {
            shared = new MeshObject(meshes_[mesh], materials_[material], edge_aspects_, materials_);
        }
        occ::handle<ConnectedMesh> connected = new ConnectedMesh();
        connected->Connect(shared, *placement);
        instance = connected;
    } else {
        // Stretched or sheared: a copy of the triangles of its own.
        instance = new MeshObject(transform_mesh(meshes_[mesh], transform), materials_[material], edge_aspects_,
                                  materials_);
    }
    return instance;
}

int mtk::Viewer::add_instance(int mesh, int material, const double* transform) {
    const occ::handle<AIS_InteractiveObject> instance = make_instance(mesh, material, transform);
    context_->Display(instance, MeshObject::shaded_mode, 0, false); // shown, and picked as a whole
    instances_.push_back(instance);
    instance_meshes_.push_back(mesh);
    return instance_count() - 1;
}

void mtk::Viewer::set_instance(int index, int mesh, int material, const double* transform) {
    forget_highlighted(index);
    const occ::handle<AIS_InteractiveObject> instance = make_instance(mesh, material, transform);
    const bool faded = instances_[index]->ZLayer() == faded_layer_;
    context_->Remove(instances_[index], false);
    context_->Display(instance, faded ? MeshObject::faded_mode : MeshObject::shaded_mode, 0, false);
    if (faded) {
        context_->SetZLayer(instance, faded_layer_);
    }
    instances_[index] = instance;
    instance_meshes_[index] = mesh;
    view_->Invalidate();
}

void mtk::Viewer::remove_instance(int index) {
    forget_highlighted(index);
    context_->Remove(instances_[index], false);
    instances_[index].Nullify();
    instance_meshes_[index] = -1;
    view_->Invalidate();
}

bool mtk::Viewer::is_placed(int mesh) const {
    return std::find(instance_meshes_.begin(), instance_meshes_.end(), mesh) != instance_meshes_.end();
}

void mtk::Viewer::remove_mesh(int mesh) {
    meshes_[mesh] = Mesh{};
    std::erase_if(shared_meshes_, [mesh](const auto& entry) { return entry.first.first == mesh; });
}

void mtk::Viewer::set_selection(const int32_t* instances, int count) {
    std::vector<bool> selected(instances_.size(), false);
    for (int i = 0; i < count; ++i) {
        selected[instances[i]] = true;
    }
    const auto selected_count = std::count(selected.begin(), selected.end(), true);
    const auto shown_count = std::count_if(instances_.begin(), instances_.end(), [](const auto& i) { return !i.IsNull(); });
    const bool fading = selected_count > 0 && selected_count < shown_count;

    for (size_t index = 0; index < instances_.size(); ++index) {
        const occ::handle<AIS_InteractiveObject>& instance = instances_[index];
        if (instance.IsNull()) {
            continue;
        }
        const bool faded = fading && !selected[index];
        if (faded != (instance->ZLayer() == faded_layer_)) {
            context_->SetZLayer(instance, faded ? faded_layer_ : Graphic3d_ZLayerId_Default);
            context_->SetDisplayMode(instance, faded ? MeshObject::faded_mode : MeshObject::shaded_mode, false);
        }
    }
    view_->Invalidate();
}

void mtk::Viewer::clear() {
    set_highlighted_elements(nullptr, 0);
    set_overlay({});
    for (const occ::handle<AIS_InteractiveObject>& instance : instances_) {
        if (!instance.IsNull()) {
            context_->Remove(instance, false);
        }
    }
    instances_.clear();
    instance_meshes_.clear();
    pick_.reset();
    shared_meshes_.clear();
    materials_.clear();
    textures_.clear();
    meshes_.clear();
    set_grid_step(default_grid_step);
    view_->Invalidate();
}

void mtk::Viewer::set_overlay(Overlay overlay) {
    if (!overlay_object_.IsNull()) {
        context_->Remove(overlay_object_, false);
        overlay_object_.Nullify();
    }
    overlay_ = std::move(overlay);
    if (!overlay_.empty()) {
        overlay_object_ = new OverlayObject(overlay_, scale_);
        overlay_object_->SetZLayer(Graphic3d_ZLayerId_Topmost);
        context_->Display(overlay_object_, 0, -1, false); // never picked
    }
    view_->Invalidate();
}

void mtk::Viewer::set_window_band(bool shown, double x0, double y0, double x1, double y1, bool crossing) {
    if (!shown) {
        if (!window_band_.IsNull() && window_band_->HasInteractiveContext()) {
            context_->Remove(window_band_, false);
        }
        view_->Invalidate();
        return;
    }
    const Quantity_Color line(0.18, 0.43, 0.87, Quantity_TOC_sRGB);
    if (window_band_.IsNull()) {
        window_band_ = new AIS_RubberBand(line, Aspect_TOL_SOLID, line, 0.85, 1.0);
        window_band_->SetZLayer(Graphic3d_ZLayerId_TopOSD);
        window_band_->SetTransformPersistence(new Graphic3d_TransformPers(Graphic3d_TMF_2d, Aspect_TOTP_LEFT_UPPER));
        window_band_->SetDisplayMode(0);
        window_band_->SetMutable(true);
    }
    window_band_->SetLineType(crossing ? Aspect_TOL_DASH : Aspect_TOL_SOLID);
    const NCollection_Vec2<int> a = to_pixels(x0, y0), b = to_pixels(x1, y1);
    // Anchored at the top-left corner of the view, upwards.
    window_band_->SetRectangle(std::min(a.x(), b.x()), -std::max(a.y(), b.y()), std::max(a.x(), b.x()),
                               -std::min(a.y(), b.y()));
    if (window_band_->HasInteractiveContext()) {
        context_->Redisplay(window_band_, false);
    } else {
        context_->Display(window_band_, 0, -1, false); // never picked
    }
    view_->Invalidate();
}

void mtk::Viewer::project(const double* points, int count, double* out_points) const {
    int width = 0, height = 0;
    if (is_attached()) {
        window_->Size(width, height);
    }
    const occ::handle<Graphic3d_Camera>& camera = view_->Camera();
    const gp_Dir forward = camera->Direction();
    for (int i = 0; i < count; ++i) {
        const gp_Pnt point(points[3 * i], points[3 * i + 1], points[3 * i + 2]);
        double* out = out_points + 2 * i;
        if (width == 0 || scale_ <= 0.0 ||
            (!camera->IsOrthographic() && gp_Vec(camera->Eye(), point).Dot(gp_Vec(forward)) <= 0.0)) {
            out[0] = out[1] = std::numeric_limits<double>::quiet_NaN();
            continue;
        }
        const gp_Pnt projected = camera->Project(point);
        out[0] = (projected.X() + 1.0) * 0.5 * width / scale_;
        out[1] = (1.0 - projected.Y()) * 0.5 * height / scale_;
    }
}

void mtk::Viewer::set_grid_step(double step) {
    grid_step_ = step;
    grid_.SetScale(1.0 / step);
    if (grid_visible_) {
        view_->GridDisplay(grid_);
    }
}

void mtk::Viewer::fit_all() {
    // The grid follows the size of the scene, which is shown whole now.
    Bnd_Box scene;
    for (const occ::handle<AIS_InteractiveObject>& instance : instances_) {
        if (instance.IsNull()) {
            continue;
        }
        Bnd_Box box;
        instance->BoundingBox(box);
        scene.Add(box);
    }
    set_grid_step(scene.IsVoid() ? default_grid_step : grid_step_for(std::sqrt(scene.SquareExtent())));

    // Without a window the view has no size: attach() fits it then.
    if (is_attached() && !instances_.empty()) {
        view_->FitAll(fit_margin, false);
    }
}

void mtk::Viewer::show_view(mtk_viewer_view view) {
    static constexpr V3d_TypeOfOrientation orientations[] = {
        V3d_TypeOfOrientation_Zup_Top,   V3d_TypeOfOrientation_Zup_Bottom, V3d_TypeOfOrientation_Zup_Front,
        V3d_TypeOfOrientation_Zup_Back,  V3d_TypeOfOrientation_Zup_Left,   V3d_TypeOfOrientation_Zup_Right,
        V3d_TypeOfOrientation_Zup_AxoRight};

    // The animation goes from the current camera to the one of the view, fitted as fit_all() does.
    occ::handle<Graphic3d_Camera> start = new Graphic3d_Camera();
    start->Copy(view_->Camera());
    view_->SetProj(orientations[view]);
    if (is_attached() && !instances_.empty()) {
        view_->FitAll(fit_margin, false);
    }
    occ::handle<Graphic3d_Camera> end = new Graphic3d_Camera();
    end->Copy(view_->Camera());
    view_->Camera()->Copy(start);

    myViewAnimation->SetView(view_);
    myViewAnimation->SetCameraStart(start);
    myViewAnimation->SetCameraEnd(end);
    myViewAnimation->SetOwnDuration(view_cube_->Duration());
    myViewAnimation->StartTimer(0.0, 1.0, true, false);
    view_->Invalidate();
}

mtk::EdgeStyle mtk::Viewer::edge_style() const {
    if (display_mode_ == MTK_VIEWER_DISPLAY_RENDERED) {
        return EdgeStyle{false, false, false, 1.0f, edge_color_}; // as things look, without lines
    }
    const bool wireframe = display_mode_ == MTK_VIEWER_DISPLAY_WIREFRAME;
    if (render_style_ == MTK_VIEWER_RENDER_CAD) {
        // The edges of their own outline the faces in every mode; without faces, they keep the colour that stands out
        // from the ground, and the meshes without edges show those of their triangles.
        return EdgeStyle{wireframe, true, !wireframe, static_cast<float>(outline_width_ * scale_),
                         wireframe ? edge_color_ : outline_color_, true};
    }
    // The edges are drawn in the modes with edges, those of their own where meshes have them, else those of the
    // triangles, one unit wide, as the lines that the tools draw over the view; the meshes made of lines only in every
    // mode, as they would otherwise not show.
    const bool edges = display_mode_ != MTK_VIEWER_DISPLAY_SHADED;
    return EdgeStyle{edges, edges, true, static_cast<float>(edge_width_ * scale_), edge_color_, true};
}

// The CAD render style lights the faces from the upper left of the camera, so that faces turned differently take
// different shades, and much from all around, so that none is dark.
void mtk::Viewer::apply_lights() {
    if (render_style_ != MTK_VIEWER_RENDER_CAD) {
        viewer_->SetDefaultLights();
        viewer_->SetLightOn();
        return;
    }
    while (!viewer_->DefinedLights().IsEmpty()) {
        viewer_->DelLight(viewer_->DefinedLights().First());
    }
    occ::handle<V3d_AmbientLight> ambient = new V3d_AmbientLight(Quantity_NOC_WHITE);
    ambient->SetIntensity(1.0f);
    occ::handle<V3d_DirectionalLight> key = new V3d_DirectionalLight(gp_Dir(0.45, -0.6, -0.66), Quantity_NOC_WHITE);
    key->SetHeadlight(true);
    viewer_->AddLight(ambient);
    viewer_->AddLight(key);
    viewer_->SetLightOn();
}

void mtk::Viewer::set_render_style(mtk_viewer_render_style style) {
    render_style_ = style;
    for (MaterialAspects& aspects : materials_) {
        apply_render_style(aspects, style);
    }
    apply_lights();
    set_display_mode(display_mode_); // with the edges, and the presentations synchronized
}

void mtk::Viewer::set_display_mode(mtk_viewer_display_mode mode) {
    const bool was_rendered = display_mode_ == MTK_VIEWER_DISPLAY_RENDERED;
    display_mode_ = mode;
    if (was_rendered || mode == MTK_VIEWER_DISPLAY_RENDERED) {
        apply_environment();
    }
    const EdgeStyle edges = edge_style();
    for (MaterialAspects& aspects : materials_) {
        apply_display_mode(aspects, mode, edges);
    }
    apply_display_mode(edge_aspects_, edges);
    // The instances placed by a transformation show the presentations of the shared meshes.
    for (const auto& [key, mesh] : shared_meshes_) {
        mesh->SynchronizeAspects();
    }
    for (const occ::handle<AIS_InteractiveObject>& instance : instances_) {
        if (!instance.IsNull() && !instance->IsKind(STANDARD_TYPE(AIS_ConnectedInteractive))) {
            instance->SynchronizeAspects();
        }
    }
    view_->Invalidate();
}

void mtk::Viewer::set_perspective(bool perspective) {
    view_->Camera()->SetProjectionType(perspective ? Graphic3d_Camera::Projection_Perspective
                                                   : Graphic3d_Camera::Projection_Orthographic);
    view_->Invalidate();
}

namespace {

// The ground of the rendered display mode: a square, lit, which receives the shadows; never selected.
class Ground : public AIS_InteractiveObject {
public:
    Ground(double height, double size) : height_(height), size_(size) {}

    bool AcceptDisplayMode(const int mode) const override { return mode == 0; }

    void Compute(const occ::handle<PrsMgr_PresentationManager>&, const occ::handle<Prs3d_Presentation>& presentation,
                 const int) override {
        occ::handle<Graphic3d_ArrayOfTriangles> square =
            new Graphic3d_ArrayOfTriangles(4, 6, Graphic3d_ArrayFlags_VertexNormal);
        const double half = size_ / 2.0;
        const gp_Dir up(0.0, 0.0, 1.0);
        square->AddVertex(gp_Pnt(-half, -half, height_), up);
        square->AddVertex(gp_Pnt(half, -half, height_), up);
        square->AddVertex(gp_Pnt(half, half, height_), up);
        square->AddVertex(gp_Pnt(-half, half, height_), up);
        square->AddEdges(1, 2, 3);
        square->AddEdges(1, 3, 4);

        const Quantity_Color color(0.36, 0.42, 0.30, Quantity_TOC_sRGB); // a dry lawn
        Graphic3d_MaterialAspect material(Graphic3d_NameOfMaterial_UserDefined);
        material.SetColor(color);
        material.SetSpecularColor(Quantity_NOC_BLACK);
        occ::handle<Graphic3d_AspectFillArea3d> aspect = new Graphic3d_AspectFillArea3d();
        aspect->SetInteriorStyle(Aspect_IS_SOLID);
        aspect->SetFrontMaterial(material);
        aspect->SetBackMaterial(material);
        const occ::handle<Graphic3d_Group> group = presentation->NewGroup();
        group->SetGroupPrimitivesAspect(aspect);
        group->AddPrimitiveArray(square);
    }

    void ComputeSelection(const occ::handle<SelectMgr_Selection>&, const int) override {}

private:
    double height_;
    double size_;
};

} // namespace

void mtk::Viewer::set_environment(const mtk_viewer_environment& environment) {
    environment_ = environment;
    if (display_mode_ == MTK_VIEWER_DISPLAY_RENDERED) {
        apply_environment();
    }
}

// The rendered display mode shows the model under a sky, a cubemap made from the sun and the clouds, on a ground in
// place of the grid, lit as in the other modes, without the ViewCube nor highlights. The other modes go back to the
// background, the grid and the ViewCube of the style.
void mtk::Viewer::apply_environment() {
    // Rendered, the view is only looked at: what is under the pointer is found, not highlighted.
    const bool rendered = display_mode_ == MTK_VIEWER_DISPLAY_RENDERED;
    if (context_->AutomaticHilight() == rendered) {
        context_->ClearDetected(false);
        context_->SetAutomaticHilight(!rendered);
    }
    if (!ground_.IsNull()) {
        context_->Remove(ground_, false);
        ground_.Nullify();
    }
    if (display_mode_ != MTK_VIEWER_DISPLAY_RENDERED) {
        view_->SetBackgroundCubeMap(occ::handle<Graphic3d_CubeMap>(), false);
        view_->SetBgGradientColors(background_top_, background_bottom_, Aspect_GradientFillMethod_Vertical);
        set_grid_visible(grid_visible_);
        context_->Display(view_cube_, false);
        return;
    }

    const bool same_sky = !sky_.IsNull() && sky_of_.cloudiness == environment_.cloudiness
        && std::equal(std::begin(sky_of_.sun_direction), std::end(sky_of_.sun_direction),
                      std::begin(environment_.sun_direction));
    if (!same_sky) {
        const gp_Dir sun(environment_.sun_direction[0], environment_.sun_direction[1], environment_.sun_direction[2]);
        sky_ = make_sky(sun, environment_.cloudiness);
        sky_of_ = environment_;
    }
    // Put in the view once it has a window: OpenCASCADE sets it up with the OpenGL context of the window.
    if (!window_.IsNull()) {
        view_->SetBackgroundCubeMap(sky_, false);
    }
    view_->GridErase();
    context_->Erase(vertical_axis_, false);
    context_->Erase(view_cube_, false); // its corner holds the time of day of the application

    ground_ = new Ground(environment_.ground_height, environment_.ground_size);
    context_->Display(ground_, 0, -1, false);
    view_->Invalidate();
}

mtk_viewer_camera mtk::Viewer::camera() const {
    const occ::handle<Graphic3d_Camera>& camera = view_->Camera();
    mtk_viewer_camera result{};
    const gp_Pnt eye = camera->Eye(), target = camera->Center();
    const gp_Dir up = camera->Up();
    for (int axis = 0; axis < 3; ++axis) {
        result.eye[axis] = eye.Coord(axis + 1);
        result.target[axis] = target.Coord(axis + 1);
        result.up[axis] = up.Coord(axis + 1);
    }
    result.field_of_view = camera->FOVy();
    return result;
}

void mtk::Viewer::set_camera(const mtk_viewer_camera& camera) {
    // Stopped first: an animation of the ViewCube would turn the camera back.
    if (!myViewAnimation.IsNull()) {
        myViewAnimation->Stop();
    }
    const occ::handle<Graphic3d_Camera>& view_camera = view_->Camera();
    view_camera->SetEyeAndCenter(gp_Pnt(camera.eye[0], camera.eye[1], camera.eye[2]),
                                 gp_Pnt(camera.target[0], camera.target[1], camera.target[2]));
    view_camera->SetUp(gp_Dir(camera.up[0], camera.up[1], camera.up[2]));
    view_camera->OrthogonalizeUp();
    view_camera->SetFOVy(camera.field_of_view);
    view_->Invalidate();
}

void mtk::Viewer::set_line_widths(double edge, double outline) {
    edge_width_ = edge;
    outline_width_ = outline;
    set_display_mode(display_mode_); // the edges are drawn again, with the new widths
}

void mtk::Viewer::set_grid_visible(bool visible) {
    grid_visible_ = visible;
    if (display_mode_ == MTK_VIEWER_DISPLAY_RENDERED) {
        return; // the ground takes its place
    }
    if (visible) {
        view_->GridDisplay(grid_);
        context_->Display(vertical_axis_, 0, -1, false);
    } else {
        view_->GridErase();
        context_->Erase(vertical_axis_, false);
    }
    view_->Invalidate();
}

bool mtk::Viewer::mouse_buttons(double x, double y, uint32_t buttons, uint32_t modifiers) {
    return is_attached()
        && UpdateMouseButtons(to_pixels(x, y), to_occt_buttons(buttons), to_occt_modifiers(modifiers), false);
}

bool mtk::Viewer::mouse_move(double x, double y, uint32_t buttons, uint32_t modifiers) {
    return is_attached()
        && UpdateMousePosition(to_pixels(x, y), to_occt_buttons(buttons), to_occt_modifiers(modifiers), false);
}

bool mtk::Viewer::mouse_leave() {
    if (!is_attached()) {
        return false;
    }
    ResetPreviousMoveTo();
    if (!context_->ClearDetected(false)) {
        return false;
    }
    view_->InvalidateImmediate();
    return true;
}

bool mtk::Viewer::scroll(double x, double y, double delta) {
    return is_attached() && UpdateMouseScroll(Aspect_ScrollDelta(to_pixels(x, y), delta));
}

bool mtk::Viewer::take_pick(mtk_viewer_pick& pick) {
    if (!pick_) {
        return false;
    }
    pick = *pick_;
    pick_.reset();
    return true;
}

occ::handle<mtk::ElementOwner> mtk::Viewer::find_element(const mtk_viewer_element& element) const {
    if (!has_instance(element.instance)) {
        return nullptr;
    }
    // The owners are those of the selection of the instance, made when it was displayed.
    const occ::handle<AIS_InteractiveObject>& instance = instances_[element.instance];
    if (!instance->HasSelection(0)) {
        return nullptr;
    }
    for (const occ::handle<SelectMgr_SensitiveEntity>& entity : instance->Selection(0)->Entities()) {
        const occ::handle<ElementOwner> owner =
            occ::down_cast<ElementOwner>(entity->BaseSensitive()->OwnerId());
        if (!owner.IsNull() && owner->kind() == element.kind && owner->index() == element.index) {
            return owner;
        }
    }
    return nullptr;
}

void mtk::Viewer::set_highlighted_elements(const mtk_viewer_element* elements, int count) {
    for (const auto& [instance, owner] : highlighted_) {
        owner->unhighlight_selected();
    }
    highlighted_.clear();
    const occ::handle<Prs3d_Drawer>& style = context_->HighlightStyle(Prs3d_TypeOfHighlight_Selected);
    for (int i = 0; i < count; ++i) {
        const occ::handle<ElementOwner> owner = find_element(elements[i]);
        owner->highlight_selected(context_->MainPrsMgr(), style);
        highlighted_.emplace_back(elements[i].instance, owner);
    }
    view_->Invalidate();
}

void mtk::Viewer::forget_highlighted(int instance) {
    std::erase_if(highlighted_, [instance](const auto& entry) {
        if (entry.first != instance) {
            return false;
        }
        entry.second->unhighlight_selected();
        return true;
    });
}

// What the context detects under the pointer: an instance, possibly one of its elements, or nothing; no value for the
// ViewCube, which is not part of the scene.
mtk_viewer_pick mtk::Viewer::nothing_picked() const {
    mtk_viewer_pick pick{};
    pick.instance = -1;
    pick.kind = MTK_VIEWER_ELEMENT_INSTANCE;
    if (!is_attached()) {
        return pick;
    }
    // The ray of the pointer, and that of the pointer one unit to the right: how far apart they are grows linearly
    // along the ray, from the near plane, in perspective, and stays the same in orthographic projection.
    const NCollection_Vec2<int> pixel = LastMousePosition();
    const int unit = std::max(1, static_cast<int>(std::lround(scale_)));
    double origin[3], direction[3], next_origin[3], next_direction[3];
    view_->ConvertWithProj(pixel.x(), pixel.y(), origin[0], origin[1], origin[2], direction[0], direction[1],
                           direction[2]);
    view_->ConvertWithProj(pixel.x() + unit, pixel.y(), next_origin[0], next_origin[1], next_origin[2],
                           next_direction[0], next_direction[1], next_direction[2]);
    // ConvertWithProj gives a point of the projection plane, which may be in the middle of the scene: the rays start
    // from the plane of the eye instead, along the view, so that what is in front of the camera is ahead of them.
    const occ::handle<Graphic3d_Camera>& camera = view_->Camera();
    const gp_Pnt eye = camera->Eye();
    const gp_Dir forward = camera->Direction();
    const auto make_ray = [&](const double* point, const double* along) -> std::optional<std::pair<gp_Pnt, gp_Vec>> {
        gp_Vec direction(along[0], along[1], along[2]);
        if (direction.Magnitude() <= gp::Resolution()) {
            return std::nullopt;
        }
        direction.Normalize();
        if (direction.Dot(gp_Vec(forward)) < 0.0) {
            direction.Reverse();
        }
        const gp_Pnt on_plane(point[0], point[1], point[2]);
        const gp_Pnt start = on_plane.Translated(-direction * gp_Vec(eye, on_plane).Dot(direction));
        return std::make_pair(start, direction);
    };
    const auto ray = make_ray(origin, direction), next = make_ray(next_origin, next_direction);
    if (!ray || !next) {
        return pick;
    }
    for (int i = 0; i < 3; ++i) {
        pick.ray_origin[i] = ray->first.Coord(i + 1);
        pick.ray_direction[i] = ray->second.Coord(i + 1);
    }
    pick.pixel_size[0] = ray->first.Distance(next->first);
    pick.pixel_size[1] = (next->second - ray->second).Magnitude();
    return pick;
}

std::optional<mtk_viewer_pick> mtk::Viewer::detected_pick() const {
    const occ::handle<SelectMgr_EntityOwner>& detected = context_->DetectedOwner();
    mtk_viewer_pick pick = nothing_picked();
    if (detected.IsNull()) {
        return pick;
    }
    const auto picked = std::find_if(instances_.begin(), instances_.end(), [&](const auto& instance) {
        return !instance.IsNull() && instance.get() == detected->Selectable().get();
    });
    if (picked == instances_.end()) {
        return std::nullopt;
    }
    pick.instance = static_cast<int>(picked - instances_.begin());
    if (const occ::handle<ElementOwner> element = occ::down_cast<ElementOwner>(detected)) {
        pick.kind = element->kind();
        pick.index = element->index();
    }
    // The detected owner is the first picked.
    if (context_->MainSelector()->NbPicked() > 0) {
        const SelectMgr_SortCriterion& data = context_->MainSelector()->PickedData(1);
        pick.point[0] = data.Point.X();
        pick.point[1] = data.Point.Y();
        pick.point[2] = data.Point.Z();
        if (pick.kind == MTK_VIEWER_ELEMENT_FACE || pick.kind == MTK_VIEWER_ELEMENT_INSTANCE) {
            pick.normal[0] = data.Normal.x();
            pick.normal[1] = data.Normal.y();
            pick.normal[2] = data.Normal.z();
        }
    }
    return pick;
}

mtk_viewer_pick mtk::Viewer::hovered() const {
    return detected_pick().value_or(nothing_picked());
}

// Called by FlushViewEvents for each click: on an instance, on nothing, or on the ViewCube, which has turned the view
// and leaves the pick as it is. The selection is shown by set_selection, not by the context.
void mtk::Viewer::OnSelectionChanged(const occ::handle<AIS_InteractiveContext>& context,
                                     const occ::handle<V3d_View>&) {
    if (const std::optional<mtk_viewer_pick> pick = detected_pick()) {
        pick_ = *pick;
    }
    context->ClearSelected(false);
}

bool mtk::Viewer::redraw() {
    if (!failure_.empty()) {
        throw kernel_error(failure_);
    }
    if (!is_attached()) {
        return false;
    }
    needs_next_frame_ = false;
    FlushViewEvents(context_, view_, true);
    return needs_next_frame_;
}

// FlushViewEvents() clears the request for another frame before it returns, having passed it to the window, which
// cannot do anything with it here: the request is kept for redraw() to return.
void mtk::Viewer::handleViewRedraw(const occ::handle<AIS_InteractiveContext>& context,
                                   const occ::handle<V3d_View>& view) {
    AIS_ViewController::handleViewRedraw(context, view);
    needs_next_frame_ = needs_next_frame_ || toAskNextFrame();
}

const std::string& mtk::Viewer::diagnostics() {
    std::ostringstream text;
    const NCollection_Vec2<int> size = native_view_->pixel_size();
    text << "Size: " << size.x() << " x " << size.y() << " pixels\n";
    text << "Scale: " << native_view_->scale() << "\n";
    if (!failure_.empty()) {
        text << "Failure: " << failure_ << "\n";
    } else if (is_attached()) {
        NCollection_IndexedDataMap<TCollection_AsciiString, TCollection_AsciiString> information;
        view_->DiagnosticInformation(information, Graphic3d_DiagnosticInfo_Basic);
        for (NCollection_IndexedDataMap<TCollection_AsciiString, TCollection_AsciiString>::Iterator item(information);
             item.More(); item.Next()) {
            text << item.Key().ToCString() << ": " << item.Value().ToCString() << "\n";
        }
    } else {
        text << "OpenGL: not attached yet, the view is not in a window\n";
    }
    diagnostics_ = text.str();
    return diagnostics_;
}

void mtk::Viewer::attach() {
    if (!failure_.empty() || !native_view_->is_ready()) {
        return;
    }

    occ::handle<Aspect_NeutralWindow> window = new Aspect_NeutralWindow();
    window->SetNativeHandle(reinterpret_cast<Aspect_Drawable>(native_view_->handle()));
    const NCollection_Vec2<int> size = native_view_->pixel_size();
    window->SetSize(size.x(), size.y());
    view_->SetWindow(window);
    window_ = window;
    // Across the longer side of the window, which the view knows now, or around the scene.
    view_->SetSize(initial_view_size);
    fit_all();

    apply_scale(native_view_->scale());
    if (display_mode_ == MTK_VIEWER_DISPLAY_RENDERED) {
        apply_environment();
    }
    view_->Redraw();
}

void mtk::Viewer::update_size() {
    const double scale = native_view_->scale();
    const bool rescaled = scale != scale_;
    if (rescaled) {
        apply_scale(scale);
    }
    const NCollection_Vec2<int> size = native_view_->pixel_size();
    if (window_->SetSize(size.x(), size.y()) || rescaled) {
        view_->MustBeResized();
    }
}

// Objects placed in pixels, like the ViewCube and the axes, and the heights of the texts follow the scale of the
// display, so that they keep their size in device-independent units.
void mtk::Viewer::apply_scale(double scale) {
    scale_ = scale;
    context_->SetPixelTolerance(static_cast<int>(std::lround(pick_tolerance * scale)));
    view_->ChangeRenderingParams().Resolution = static_cast<unsigned int>(std::lround(72.0 * scale));

    // The distance of the axes from their corner follows their scale, but only when the corner is set again.
    view_->Trihedron()->SetScale(axes_size * scale);
    view_->Trihedron()->SetPosition(Aspect_TOTP_LEFT_LOWER);
    view_->Trihedron()->Display(*view_);

    // SetSize adapts the bevels and the labels to the size: they are set afterwards.
    const double size = view_cube_size * scale;
    view_cube_->SetSize(size);
    view_cube_->SetBoxFacetExtension(size * view_cube_bevel);
    view_cube_->SetFontHeight(size * view_cube_text);
    const int offset = static_cast<int>(std::lround(view_cube_offset * scale));
    view_cube_->SetTransformPersistence(new Graphic3d_TransformPers(
        navigation_aid_persistence(), Aspect_TOTP_RIGHT_UPPER, NCollection_Vec2<int>(offset, offset)));
    if (context_->IsDisplayed(view_cube_)) {
        context_->Redisplay(view_cube_, false);
    }
    set_display_mode(display_mode_); // the width of the outlines

    // The elements draw their edges as wide as the lines of the highlight styles, the selected ones again.
    for (const Prs3d_TypeOfHighlight type : {Prs3d_TypeOfHighlight_Dynamic, Prs3d_TypeOfHighlight_Selected}) {
        context_->HighlightStyle(type)->SetLineAspect(
            new Prs3d_LineAspect(Quantity_NOC_WHITE, Aspect_TOL_SOLID, highlighted_edge_width * scale));
    }
    for (const auto& [instance, owner] : highlighted_) {
        owner->highlight_selected(context_->MainPrsMgr(), context_->HighlightStyle(Prs3d_TypeOfHighlight_Selected));
    }
    if (!overlay_object_.IsNull()) {
        set_overlay(overlay_); // in sizes of the new scale
    }
}

NCollection_Vec2<int> mtk::Viewer::to_pixels(double x, double y) const {
    return NCollection_Vec2<int>(static_cast<int>(std::lround(x * scale_)), static_cast<int>(std::lround(y * scale_)));
}

namespace {

// Checks the arguments shared by the input functions.
mtk_status check_input(const mtk_viewer* viewer, int32_t* out_redraw) {
    mtk::clear_outputs(out_redraw);
    if (viewer == nullptr || out_redraw == nullptr) {
        return mtk::invalid_argument("viewer and out_redraw must not be null.");
    }
    return MTK_STATUS_OK;
}

mtk_status check_position(double x, double y) {
    if (!std::isfinite(x) || !std::isfinite(y)) {
        return mtk::invalid_argument("The position must be finite.");
    }
    return MTK_STATUS_OK;
}

// The faces, edges and vertices of a mesh whose triangles and edges are checked.
mtk_status check_elements(const mtk_viewer_mesh& mesh) {
    if (mesh.face_first_triangles == nullptr) {
        return MTK_STATUS_OK;
    }
    if (mesh.face_count < (mesh.triangle_count > 0 ? 1 : 0) || mesh.face_count > mesh.triangle_count + 1 ||
        mesh.face_first_triangles[0] != 0 ||
        mesh.face_first_triangles[mesh.face_count] != mesh.triangle_count) {
        return mtk::invalid_argument("The faces of a mesh must take its triangles from 0 to the last one.");
    }
    for (int32_t face = 0; face < mesh.face_count; ++face) {
        if (mesh.face_first_triangles[face + 1] < mesh.face_first_triangles[face]) {
            return mtk::invalid_argument("The faces of a mesh must take its triangles in order.");
        }
    }
    if (mesh.edge_first_points == nullptr || (mesh.edge_count > 0 && mesh.edge_indices == nullptr)) {
        return mtk::invalid_argument("A mesh with faces needs edges of its own, with their indices in the shape.");
    }
    for (int32_t edge = 0; edge < mesh.edge_count; ++edge) {
        if (mesh.edge_indices[edge] < 0) {
            return mtk::invalid_argument("The indices of the edges in the shape must not be negative.");
        }
    }
    if (mesh.shape_vertex_count < 0 || mesh.shape_vertex_count > max_mesh_size ||
        (mesh.shape_vertex_count > 0 && mesh.shape_vertices == nullptr)) {
        return mtk::invalid_argument("The vertices of the shape must be from 0 to 715827882, and not null.");
    }
    for (int64_t i = 0; i < 3 * static_cast<int64_t>(mesh.shape_vertex_count); ++i) {
        if (!std::isfinite(mesh.shape_vertices[i])) {
            return mtk::invalid_argument("The vertices of the shape must be finite.");
        }
    }
    return MTK_STATUS_OK;
}

mtk_status check_mesh(const mtk_viewer_mesh& mesh) {
    // A mesh of edges only, such as lines drawn on the ground, has no triangles.
    const bool edges_only = mesh.vertex_count == 0 && mesh.triangle_count == 0 && mesh.edge_first_points != nullptr &&
                            mesh.edge_count > 0;
    if (!edges_only && (mesh.vertex_count <= 0 || mesh.triangle_count <= 0 || mesh.vertex_count > max_mesh_size ||
                        mesh.triangle_count > max_mesh_size)) {
        return mtk::invalid_argument(
            "A mesh needs from 1 to 715827882 vertices and triangles, or none and edges of its own.");
    }
    if (!edges_only && (mesh.positions == nullptr || mesh.triangles == nullptr)) {
        return mtk::invalid_argument("The positions and the triangles of a mesh must not be null.");
    }
    for (int32_t i = 0; i < 3 * mesh.vertex_count; ++i) {
        if (!std::isfinite(mesh.positions[i])) {
            return mtk::invalid_argument("The positions of a mesh must be finite.");
        }
    }
    for (int32_t i = 0; i < 3 * mesh.triangle_count; ++i) {
        if (mesh.triangles[i] < 0 || mesh.triangles[i] >= mesh.vertex_count) {
            return mtk::invalid_argument("A triangle refers to a vertex that the mesh does not have.");
        }
    }

    if (mesh.edge_first_points == nullptr) {
        return check_elements(mesh);
    }
    if (mesh.edge_count < 0 || mesh.edge_count > max_mesh_size || mesh.edge_first_points[0] != 0) {
        return mtk::invalid_argument("The edges of a mesh must be from 0 to 715827882, their points counted from 0.");
    }
    for (int32_t edge = 0; edge < mesh.edge_count; ++edge) {
        const int64_t first = mesh.edge_first_points[edge], next = mesh.edge_first_points[edge + 1];
        if (next - first < 2 || next > max_mesh_size) {
            return mtk::invalid_argument("Each edge of a mesh needs two points or more, and 715827882 points at most.");
        }
    }
    const int32_t point_count = mesh.edge_first_points[mesh.edge_count];
    if (point_count > 0 && mesh.edge_points == nullptr) {
        return mtk::invalid_argument("The points of the edges of a mesh must not be null.");
    }
    for (int64_t i = 0; i < 3 * static_cast<int64_t>(point_count); ++i) {
        if (!std::isfinite(mesh.edge_points[i])) {
            return mtk::invalid_argument("The points of the edges of a mesh must be finite.");
        }
    }
    return check_elements(mesh);
}

bool is_unit(float value) {
    return value >= 0.0f && value <= 1.0f; // false for NaN
}

mtk_status check_material(const mtk_viewer_material& material, int texture_count) {
    if (!is_unit(material.base_color[0]) || !is_unit(material.base_color[1]) || !is_unit(material.base_color[2]) ||
        !is_unit(material.base_color[3]) || !is_unit(material.metallic) || !is_unit(material.roughness)) {
        return mtk::invalid_argument("The colour, metallic and roughness of a material must be between 0 and 1.");
    }
    if (material.base_color_texture < -1 || material.base_color_texture >= texture_count) {
        return mtk::invalid_argument("The texture of a material must be one added to the viewer, or -1.");
    }
    return MTK_STATUS_OK;
}

mtk_status check_transform(const double* transform) {
    if (transform == nullptr) {
        return mtk::invalid_argument("transform must not be null.");
    }
    for (int i = 0; i < 12; ++i) {
        if (!std::isfinite(transform[i])) {
            return mtk::invalid_argument("The values of a transform must be finite.");
        }
    }
    const double determinant = transform[0] * (transform[5] * transform[10] - transform[6] * transform[9]) -
                               transform[1] * (transform[4] * transform[10] - transform[6] * transform[8]) +
                               transform[2] * (transform[4] * transform[9] - transform[5] * transform[8]);
    if (!(std::abs(determinant) > std::numeric_limits<double>::min())) {
        return mtk::invalid_argument("A transform must not flatten the mesh.");
    }
    return MTK_STATUS_OK;
}

} // namespace

int32_t mtk_viewer_abi_version(void) {
    return MTK_OCCT_VIEWER_ABI_VERSION;
}

const char* mtk_viewer_last_error_message(void) {
    return mtk::last_error_message();
}

mtk_status mtk_viewer_create(void* parent, mtk_viewer** out_viewer) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_viewer);
        if (out_viewer == nullptr) {
            return mtk::invalid_argument("out_viewer must not be null.");
        }
#if defined(_WIN32)
        if (parent == nullptr) {
            return mtk::invalid_argument("parent must be the window that will contain the view.");
        }
#endif
        *out_viewer = new mtk_viewer(parent);
        return MTK_STATUS_OK;
    });
}

void mtk_viewer_release(mtk_viewer* viewer) {
    delete viewer;
}

mtk_status mtk_viewer_get_native_view(const mtk_viewer* viewer, void** out_view) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_view);
        if (viewer == nullptr || out_view == nullptr) {
            return mtk::invalid_argument("viewer and out_view must not be null.");
        }
        *out_view = viewer->native_view();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_colors(mtk_viewer* viewer, const mtk_viewer_colors* colors) {
    return mtk::guarded([&] {
        if (viewer == nullptr || colors == nullptr) {
            return mtk::invalid_argument("viewer and colors must not be null.");
        }
        for (const float(*color)[3] : {&colors->background_top, &colors->background_bottom, &colors->grid,
                                       &colors->axis_x, &colors->axis_y, &colors->axis_z, &colors->cube_face,
                                       &colors->cube_edge, &colors->cube_text, &colors->highlight,
                                       &colors->edges, &colors->outlines}) {
            if (!is_color(*color)) {
                return mtk::invalid_argument("Colour components must be between 0 and 1.");
            }
        }
        viewer->set_colors(*colors);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_add_mesh(mtk_viewer* viewer, const mtk_viewer_mesh* mesh, int32_t* out_mesh) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_mesh);
        if (viewer == nullptr || mesh == nullptr || out_mesh == nullptr) {
            return mtk::invalid_argument("viewer, mesh and out_mesh must not be null.");
        }
        const mtk_status status = check_mesh(*mesh);
        if (status != MTK_STATUS_OK) {
            return status;
        }
        if (mesh->face_first_triangles != nullptr && mesh->face_materials != nullptr) {
            for (int32_t face = 0; face < mesh->face_count; ++face) {
                if (mesh->face_materials[face] < -1 || mesh->face_materials[face] >= viewer->material_count()) {
                    return mtk::invalid_argument("The material of a face must be one of the viewer, or -1.");
                }
            }
        }
        *out_mesh = viewer->add_mesh(*mesh);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_add_texture(mtk_viewer* viewer, const uint8_t* pixels, int32_t width, int32_t height,
                                  int32_t* out_texture) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_texture);
        if (viewer == nullptr || pixels == nullptr || out_texture == nullptr) {
            return mtk::invalid_argument("viewer, pixels and out_texture must not be null.");
        }
        if (width <= 0 || height <= 0 || width > max_texture_size || height > max_texture_size) {
            return mtk::invalid_argument("A texture must have from 1 to 16384 pixels on each side.");
        }
        *out_texture = viewer->add_texture(pixels, width, height);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_add_material(mtk_viewer* viewer, const mtk_viewer_material* material, int32_t* out_material) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_material);
        if (viewer == nullptr || material == nullptr || out_material == nullptr) {
            return mtk::invalid_argument("viewer, material and out_material must not be null.");
        }
        const mtk_status status = check_material(*material, viewer->texture_count());
        if (status == MTK_STATUS_OK) {
            *out_material = viewer->add_material(*material);
        }
        return status;
    });
}

mtk_status mtk_viewer_add_instance(mtk_viewer* viewer, int32_t mesh, int32_t material, const double* transform,
                                   int32_t* out_instance) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_instance);
        if (viewer == nullptr || out_instance == nullptr) {
            return mtk::invalid_argument("viewer and out_instance must not be null.");
        }
        if (!viewer->has_mesh(mesh) || material < 0 || material >= viewer->material_count()) {
            return mtk::invalid_argument("An instance needs a mesh and a material added to the viewer.");
        }
        const mtk_status status = check_transform(transform);
        if (status == MTK_STATUS_OK) {
            *out_instance = viewer->add_instance(mesh, material, transform);
        }
        return status;
    });
}

mtk_status mtk_viewer_set_instance(mtk_viewer* viewer, int32_t instance, int32_t mesh, int32_t material,
                                   const double* transform) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        if (!viewer->has_instance(instance)) {
            return mtk::invalid_argument("The viewer does not have the instance.");
        }
        if (!viewer->has_mesh(mesh) || material < 0 || material >= viewer->material_count()) {
            return mtk::invalid_argument("An instance needs a mesh and a material added to the viewer.");
        }
        const mtk_status status = check_transform(transform);
        if (status == MTK_STATUS_OK) {
            viewer->set_instance(instance, mesh, material, transform);
        }
        return status;
    });
}

mtk_status mtk_viewer_remove_instance(mtk_viewer* viewer, int32_t instance) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        if (!viewer->has_instance(instance)) {
            return mtk::invalid_argument("The viewer does not have the instance.");
        }
        viewer->remove_instance(instance);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_remove_mesh(mtk_viewer* viewer, int32_t mesh) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        if (!viewer->has_mesh(mesh)) {
            return mtk::invalid_argument("The viewer does not have the mesh.");
        }
        if (viewer->is_placed(mesh)) {
            return mtk::invalid_argument("The mesh is placed by an instance: remove the instance first.");
        }
        viewer->remove_mesh(mesh);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_selection(mtk_viewer* viewer, const int32_t* instances, int32_t count) {
    return mtk::guarded([&] {
        if (viewer == nullptr || (instances == nullptr && count != 0)) {
            return mtk::invalid_argument("viewer must not be null, nor instances unless count is 0.");
        }
        if (count < 0) {
            return mtk::invalid_argument("count must not be negative.");
        }
        for (int32_t i = 0; i < count; ++i) {
            if (!viewer->has_instance(instances[i])) {
                return mtk::invalid_argument("The selection refers to an instance that the viewer does not have.");
            }
        }
        viewer->set_selection(instances, count);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_clear(mtk_viewer* viewer) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        viewer->clear();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_fit_all(mtk_viewer* viewer) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        viewer->fit_all();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_show_view(mtk_viewer* viewer, int32_t view) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        if (view < MTK_VIEWER_VIEW_TOP || view > MTK_VIEWER_VIEW_ISOMETRIC) {
            return mtk::invalid_argument("view is not a standard view.");
        }
        viewer->show_view(static_cast<mtk_viewer_view>(view));
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_display_mode(mtk_viewer* viewer, int32_t mode) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        if (mode < MTK_VIEWER_DISPLAY_SHADED || mode > MTK_VIEWER_DISPLAY_RENDERED) {
            return mtk::invalid_argument("mode is not a display mode.");
        }
        viewer->set_display_mode(static_cast<mtk_viewer_display_mode>(mode));
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_render_style(mtk_viewer* viewer, int32_t style) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        if (style < MTK_VIEWER_RENDER_STANDARD || style > MTK_VIEWER_RENDER_CAD) {
            return mtk::invalid_argument("style is not a render style.");
        }
        viewer->set_render_style(static_cast<mtk_viewer_render_style>(style));
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_perspective(mtk_viewer* viewer, int32_t perspective) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        viewer->set_perspective(perspective != 0);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_get_camera(const mtk_viewer* viewer, mtk_viewer_camera* out_camera) {
    return mtk::guarded([&] {
        if (viewer == nullptr || out_camera == nullptr) {
            return mtk::invalid_argument("viewer and out_camera must not be null.");
        }
        *out_camera = viewer->camera();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_camera(mtk_viewer* viewer, const mtk_viewer_camera* camera) {
    return mtk::guarded([&] {
        if (viewer == nullptr || camera == nullptr) {
            return mtk::invalid_argument("viewer and camera must not be null.");
        }
        const gp_Vec direction(camera->target[0] - camera->eye[0], camera->target[1] - camera->eye[1],
                               camera->target[2] - camera->eye[2]);
        const gp_Vec up(camera->up[0], camera->up[1], camera->up[2]);
        bool finite = std::isfinite(camera->field_of_view);
        for (int axis = 0; axis < 3; ++axis) {
            finite = finite && std::isfinite(camera->eye[axis]) && std::isfinite(camera->target[axis])
                     && std::isfinite(camera->up[axis]);
        }
        if (!finite) {
            return mtk::invalid_argument("the camera must be finite.");
        }
        if (direction.Magnitude() < 1e-9 || up.Magnitude() < 1e-9
            || direction.Crossed(up).Magnitude() < 1e-9 * direction.Magnitude() * up.Magnitude()) {
            return mtk::invalid_argument("the target must be away from the eye, and up not along the direction.");
        }
        if (camera->field_of_view < 1.0 || camera->field_of_view > 170.0) {
            return mtk::invalid_argument("the field of view must be between 1 and 170 degrees.");
        }
        viewer->set_camera(*camera);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_environment(mtk_viewer* viewer, const mtk_viewer_environment* environment) {
    return mtk::guarded([&] {
        if (viewer == nullptr || environment == nullptr) {
            return mtk::invalid_argument("viewer and environment must not be null.");
        }
        const double* sun = environment->sun_direction;
        if (!std::isfinite(sun[0]) || !std::isfinite(sun[1]) || !std::isfinite(sun[2]) ||
            std::sqrt(sun[0] * sun[0] + sun[1] * sun[1] + sun[2] * sun[2]) < 1e-9) {
            return mtk::invalid_argument("the direction of the sun must be finite and not zero.");
        }
        if (!(environment->cloudiness >= 0.0f && environment->cloudiness <= 1.0f)) {
            return mtk::invalid_argument("the cloudiness must be between 0 and 1.");
        }
        if (!std::isfinite(environment->ground_height) || !(environment->ground_size > 0.0) ||
            !std::isfinite(environment->ground_size)) {
            return mtk::invalid_argument("the ground must have a finite height and a positive size.");
        }
        viewer->set_environment(*environment);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_line_widths(mtk_viewer* viewer, double edge_width, double outline_width) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        for (const double width : {edge_width, outline_width}) {
            if (!std::isfinite(width) || width < 0.25 || width > 10.0) {
                return mtk::invalid_argument("The widths of lines must be between 0.25 and 10 pixels.");
            }
        }
        viewer->set_line_widths(edge_width, outline_width);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_grid_visible(mtk_viewer* viewer, int32_t visible) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        viewer->set_grid_visible(visible != 0);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_mouse_buttons(mtk_viewer* viewer, double x, double y, uint32_t buttons, uint32_t modifiers,
                                    int32_t* out_redraw) {
    return mtk::guarded([&] {
        mtk_status status = check_input(viewer, out_redraw);
        if (status == MTK_STATUS_OK) {
            status = check_position(x, y);
        }
        if (status == MTK_STATUS_OK) {
            *out_redraw = viewer->mouse_buttons(x, y, buttons, modifiers) ? 1 : 0;
        }
        return status;
    });
}

mtk_status mtk_viewer_mouse_move(mtk_viewer* viewer, double x, double y, uint32_t buttons, uint32_t modifiers,
                                 int32_t* out_redraw) {
    return mtk::guarded([&] {
        mtk_status status = check_input(viewer, out_redraw);
        if (status == MTK_STATUS_OK) {
            status = check_position(x, y);
        }
        if (status == MTK_STATUS_OK) {
            *out_redraw = viewer->mouse_move(x, y, buttons, modifiers) ? 1 : 0;
        }
        return status;
    });
}

mtk_status mtk_viewer_mouse_leave(mtk_viewer* viewer, int32_t* out_redraw) {
    return mtk::guarded([&] {
        const mtk_status status = check_input(viewer, out_redraw);
        if (status == MTK_STATUS_OK) {
            *out_redraw = viewer->mouse_leave() ? 1 : 0;
        }
        return status;
    });
}

mtk_status mtk_viewer_take_pick(mtk_viewer* viewer, int32_t* out_picked, mtk_viewer_pick* out_pick) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_picked, out_pick);
        if (viewer == nullptr || out_picked == nullptr || out_pick == nullptr) {
            return mtk::invalid_argument("viewer, out_picked and out_pick must not be null.");
        }
        out_pick->instance = -1;
        if (viewer->take_pick(*out_pick)) {
            *out_picked = 1;
        }
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_get_hovered(mtk_viewer* viewer, mtk_viewer_pick* out_pick) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_pick);
        if (viewer == nullptr || out_pick == nullptr) {
            return mtk::invalid_argument("viewer and out_pick must not be null.");
        }
        *out_pick = viewer->hovered();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_highlighted_elements(mtk_viewer* viewer, const mtk_viewer_element* elements,
                                               int32_t count) {
    return mtk::guarded([&] {
        if (viewer == nullptr || (elements == nullptr && count != 0)) {
            return mtk::invalid_argument("viewer must not be null, nor elements unless count is 0.");
        }
        if (count < 0) {
            return mtk::invalid_argument("count must not be negative.");
        }
        for (int32_t i = 0; i < count; ++i) {
            if (elements[i].kind == MTK_VIEWER_ELEMENT_INSTANCE || viewer->find_element(elements[i]).IsNull()) {
                return mtk::invalid_argument("An element is not a face, an edge or a vertex that an instance shows.");
            }
        }
        viewer->set_highlighted_elements(elements, count);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_overlay(mtk_viewer* viewer, const mtk_viewer_overlay* overlay) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        if (overlay == nullptr) {
            viewer->set_overlay({});
            return MTK_STATUS_OK;
        }
        std::string error;
        std::optional<mtk::Overlay> checked = mtk::make_overlay(*overlay, error);
        if (!checked) {
            return mtk::invalid_argument(error.c_str());
        }
        viewer->set_overlay(std::move(*checked));
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_set_window_band(mtk_viewer* viewer, int32_t shown, double x0, double y0, double x1, double y1,
                                      int32_t crossing) {
    return mtk::guarded([&] {
        if (viewer == nullptr) {
            return mtk::invalid_argument("viewer must not be null.");
        }
        if (shown != 0 && !(std::isfinite(x0) && std::isfinite(y0) && std::isfinite(x1) && std::isfinite(y1))) {
            return mtk::invalid_argument("The corners must be finite.");
        }
        viewer->set_window_band(shown != 0, x0, y0, x1, y1, crossing != 0);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_project(const mtk_viewer* viewer, const double* points, int32_t count, double* out_points) {
    return mtk::guarded([&] {
        if (viewer == nullptr || count < 0 || (count > 0 && (points == nullptr || out_points == nullptr))) {
            return mtk::invalid_argument("viewer must not be null, nor the points when counted, and count not negative.");
        }
        viewer->project(points, count, out_points);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_scroll(mtk_viewer* viewer, double x, double y, double delta, int32_t* out_redraw) {
    return mtk::guarded([&] {
        mtk_status status = check_input(viewer, out_redraw);
        if (status == MTK_STATUS_OK) {
            status = check_position(x, y);
        }
        if (status == MTK_STATUS_OK && !std::isfinite(delta)) {
            status = mtk::invalid_argument("delta must be finite.");
        }
        if (status == MTK_STATUS_OK) {
            *out_redraw = viewer->scroll(x, y, delta) ? 1 : 0;
        }
        return status;
    });
}

mtk_status mtk_viewer_redraw(mtk_viewer* viewer, int32_t* out_animating) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_animating);
        if (viewer == nullptr || out_animating == nullptr) {
            return mtk::invalid_argument("viewer and out_animating must not be null.");
        }
        *out_animating = viewer->redraw() ? 1 : 0;
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_get_diagnostics(mtk_viewer* viewer, const char** out_text) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_text);
        if (viewer == nullptr || out_text == nullptr) {
            return mtk::invalid_argument("viewer and out_text must not be null.");
        }
        *out_text = viewer->diagnostics().c_str();
        return MTK_STATUS_OK;
    });
}
