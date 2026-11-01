#include "viewer.hpp"

#include "mtk/errors.hpp"

#include <AIS_AnimationCamera.hxx>
#include <Aspect_DisplayConnection.hxx>
#include <Aspect_GridParams.hxx>
#include <Aspect_ScrollDelta.hxx>
#include <Graphic3d_Camera.hxx>
#include <Graphic3d_DiagnosticInfo.hxx>
#include <Graphic3d_TransformPers.hxx>
#include <NCollection_IndexedDataMap.hxx>
#include <OpenGl_GraphicDriver.hxx>
#include <Prs3d_Drawer.hxx>
#include <Prs3d_ShadingAspect.hxx>
#include <Prs3d_TextAspect.hxx>
#include <TCollection_AsciiString.hxx>
#include <V3d_Trihedron.hxx>

#include <cmath>
#include <sstream>

// Definition of the opaque handle declared in the public header.
struct mtk_viewer : mtk::Viewer {
    using mtk::Viewer::Viewer;
};

// The managed side declares the same layout: this size must match the one it checks in its tests.
static_assert(sizeof(mtk_viewer_colors) == 124);

namespace {

// Sizes in device-independent units, like the rest of the interface; the viewer multiplies them by the scale.
constexpr double view_cube_size = 42.0;
constexpr double view_cube_offset = 70.0; // from the upper right corner to the centre of the cube
constexpr double view_cube_bevel = 0.08;  // width of the edges and corners, relative to the size
constexpr double view_cube_text = 0.13;   // height of the labels, relative to the size
constexpr double axes_size = 0.07;        // unit of V3d_Trihedron: 500 pixels
constexpr double axes_label_height = 12.0;

// The camera and the ground, in millimetres.
constexpr double field_of_view = 35.0;         // vertical, in degrees
constexpr double initial_view_size = 12000.0; // the ground shown before a model is opened
constexpr double grid_step = 1000.0;

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

// The grid on the ground, drawn by a shader over the whole plane. Its lines are hidden by the objects in front of them.
Aspect_GridParams ground_grid(const Quantity_Color& color) {
    Aspect_GridParams grid;
    grid.SetColor(color);
    grid.SetScale(1.0 / grid_step); // squares per millimetre
    grid.SetIsDrawAxis(false);
    return grid;
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

    context_ = new AIS_InteractiveContext(viewer_);

    view_ = viewer_->CreateView();
    // Drawn only by redraw() and by the native view, never as a side effect of a change.
    view_->SetImmediateUpdate(false);
    view_->SetProj(V3d_TypeOfOrientation_Zup_AxoRight);
    view_->Camera()->SetProjectionType(Graphic3d_Camera::Projection_Perspective);
    view_->Camera()->SetFOVy(field_of_view);
    view_->ChangeRenderingParams().NbMsaaSamples = 4;
    view_->GridDisplay(ground_grid(Quantity_NOC_GRAY70));

    const occ::handle<V3d_Trihedron>& axes = view_->Trihedron();
    axes->SetPosition(Aspect_TOTP_LEFT_LOWER);
    const occ::handle<Graphic3d_TransformPers>& placement = axes->TransformPersistence();
    placement->SetPersistence(navigation_aid_persistence(), placement->Corner2d(), placement->Offset2d());
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
    view_->SetBgGradientColors(to_color(colors.background_top), to_color(colors.background_bottom),
                               Aspect_GradientFillMethod_Vertical);
    view_->GridDisplay(ground_grid(to_color(colors.grid)));

    const occ::handle<V3d_Trihedron>& axes = view_->Trihedron();
    const Quantity_Color x = to_color(colors.axis_x), y = to_color(colors.axis_y), z = to_color(colors.axis_z);
    axes->SetArrowsColor(x, y, z);
    axes->SetLabelsColor(x, y, z);

    const Quantity_Color face = to_color(colors.cube_face);
    view_cube_->SetBoxColor(face);
    view_cube_->SetInnerColor(face);
    view_cube_->BoxEdgeStyle()->SetColor(to_color(colors.cube_edge));
    view_cube_->BoxCornerStyle()->SetColor(to_color(colors.cube_edge));
    view_cube_->SetTextColor(to_color(colors.cube_text));
    view_cube_->SetBoxTransparency(colors.cube_transparency);
    const Quantity_Color highlight = to_color(colors.highlight);
    view_cube_->DynamicHilightAttributes()->SetColor(highlight);
    view_cube_->DynamicHilightAttributes()->ShadingAspect()->SetColor(highlight);
    context_->Redisplay(view_cube_, false);

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

bool mtk::Viewer::redraw() {
    if (!failure_.empty()) {
        throw kernel_error(failure_);
    }
    if (!is_attached()) {
        return false;
    }
    FlushViewEvents(context_, view_, true);
    return toAskNextFrame();
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
    // Across the longer side of the window, which the view knows now.
    view_->SetSize(initial_view_size);

    apply_scale(native_view_->scale());
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
    view_->ChangeRenderingParams().Resolution = static_cast<unsigned int>(std::lround(72.0 * scale));

    view_->Trihedron()->SetScale(axes_size * scale);
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
}

NCollection_Vec2<int> mtk::Viewer::to_pixels(double x, double y) const {
    return NCollection_Vec2<int>(static_cast<int>(std::lround(x * scale_)), static_cast<int>(std::lround(y * scale_)));
}

namespace {

// Checks the arguments shared by the input functions.
mtk_status check_input(const mtk_viewer* viewer, int32_t* out_redraw) {
    if (viewer == nullptr || out_redraw == nullptr) {
        return mtk::invalid_argument("viewer and out_redraw must not be null.");
    }
    *out_redraw = 0;
    return MTK_STATUS_OK;
}

mtk_status check_position(double x, double y) {
    if (!std::isfinite(x) || !std::isfinite(y)) {
        return mtk::invalid_argument("The position must be finite.");
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
        if (out_viewer == nullptr) {
            return mtk::invalid_argument("out_viewer must not be null.");
        }
        *out_viewer = nullptr;
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
                                       &colors->cube_edge, &colors->cube_text, &colors->highlight}) {
            if (!is_color(*color)) {
                return mtk::invalid_argument("Colour components must be between 0 and 1.");
            }
        }
        if (!(colors->cube_transparency >= 0.0f && colors->cube_transparency < 1.0f)) {
            return mtk::invalid_argument("The transparency of the ViewCube must be at least 0 and below 1.");
        }
        viewer->set_colors(*colors);
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
        if (viewer == nullptr || out_animating == nullptr) {
            return mtk::invalid_argument("viewer and out_animating must not be null.");
        }
        *out_animating = 0;
        *out_animating = viewer->redraw() ? 1 : 0;
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_viewer_get_diagnostics(mtk_viewer* viewer, const char** out_text) {
    return mtk::guarded([&] {
        if (viewer == nullptr || out_text == nullptr) {
            return mtk::invalid_argument("viewer and out_text must not be null.");
        }
        *out_text = nullptr;
        *out_text = viewer->diagnostics().c_str();
        return MTK_STATUS_OK;
    });
}
