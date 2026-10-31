#include "viewer.hpp"

#include "mtk/errors.hpp"

#include <AIS_AnimationCamera.hxx>
#include <Aspect_DisplayConnection.hxx>
#include <Aspect_ScrollDelta.hxx>
#include <Graphic3d_DiagnosticInfo.hxx>
#include <Graphic3d_TransformPers.hxx>
#include <NCollection_IndexedDataMap.hxx>
#include <OpenGl_GraphicDriver.hxx>
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
static_assert(sizeof(mtk_viewer_colors) == 36);

namespace {

// Sizes in device-independent units, like the rest of the interface; the viewer multiplies them by the scale.
constexpr double view_cube_size = 40.0;
constexpr double view_cube_offset = 70.0; // from the upper right corner to the centre of the cube
constexpr double axes_size = 0.08;        // unit of V3d_View::TriedronDisplay: 500 pixels
constexpr double axes_label_height = 12.0;

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
    view_->ChangeRenderingParams().NbMsaaSamples = 4;

    // Clicking a face of the cube animates the camera to that view, frame after frame (see redraw()).
    view_cube_ = new AIS_ViewCube();
    view_cube_->SetDrawAxes(false); // the axes are in the lower left corner
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
    foreground_ = to_color(colors.foreground);
    show_axes();
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
    show_axes();

    view_cube_->SetSize(view_cube_size * scale);
    const int offset = static_cast<int>(std::lround(view_cube_offset * scale));
    view_cube_->SetTransformPersistence(new Graphic3d_TransformPers(
        Graphic3d_TMF_TriedronPers, Aspect_TOTP_RIGHT_UPPER, NCollection_Vec2<int>(offset, offset)));
    if (context_->IsDisplayed(view_cube_)) {
        context_->Redisplay(view_cube_, false);
    }
}

void mtk::Viewer::show_axes() {
    const occ::handle<V3d_Trihedron>& axes = view_->Trihedron();
    for (V3d_TypeOfAxe axis : {V3d_X, V3d_Y, V3d_Z}) {
        axes->LabelAspect(axis)->SetHeight(axes_label_height);
    }
    view_->TriedronDisplay(Aspect_TOTP_LEFT_LOWER, foreground_, axes_size * scale_, V3d_ZBUFFER);
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
        if (!is_color(colors->background_top) || !is_color(colors->background_bottom)
            || !is_color(colors->foreground)) {
            return mtk::invalid_argument("Colour components must be between 0 and 1.");
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
