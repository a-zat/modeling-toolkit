#include "mtk/occt_viewer.h"

#include <gtest/gtest.h>

#include <limits>
#include <memory>
#include <string>

#if defined(_WIN32)
#include <windows.h>
#endif

// The views are never shown, so OpenCASCADE never creates an OpenGL context: the build machines may have no driver
// for it. Drawing is tried in the application.

namespace {

struct ViewerDeleter {
    void operator()(mtk_viewer* viewer) const noexcept { mtk_viewer_release(viewer); }
};

using ViewerPtr = std::unique_ptr<mtk_viewer, ViewerDeleter>;

// The window that contains the view, hidden. On macOS there is none: the application adds the view to its window.
class Parent {
public:
#if defined(_WIN32)
    Parent()
        : window_(CreateWindowExW(0, L"STATIC", L"", WS_OVERLAPPED, 0, 0, 200, 100, nullptr, nullptr,
                                  GetModuleHandleW(nullptr), nullptr)) {}
    ~Parent() { DestroyWindow(window_); }

    void* handle() const { return window_; }

private:
    HWND window_;
#else
    void* handle() const { return nullptr; }
#endif
};

ViewerPtr create_viewer(const Parent& parent) {
    mtk_viewer* viewer = nullptr;
    EXPECT_EQ(mtk_viewer_create(parent.handle(), &viewer), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    return ViewerPtr(viewer);
}

mtk_viewer_colors colors(float value) {
    mtk_viewer_colors result{};
    for (float(*color)[3] : {&result.background_top, &result.background_bottom, &result.axis_x, &result.axis_y,
                             &result.axis_z, &result.cube_face, &result.cube_edge, &result.cube_text,
                             &result.highlight}) {
        for (float& component : *color) {
            component = value;
        }
    }
    return result;
}

TEST(Viewer, ReportsItsAbiVersion) {
    EXPECT_EQ(mtk_viewer_abi_version(), MTK_OCCT_VIEWER_ABI_VERSION);
}

TEST(Viewer, CreatesItsNativeView) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    void* view = nullptr;

    ASSERT_EQ(mtk_viewer_get_native_view(viewer.get(), &view), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    EXPECT_NE(view, nullptr);
#if defined(_WIN32)
    EXPECT_EQ(GetParent(static_cast<HWND>(view)), parent.handle());
#endif
}

TEST(Viewer, IgnoresInputUntilTheViewIsInAWindow) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    int32_t redraw = 1;

    EXPECT_EQ(mtk_viewer_mouse_move(viewer.get(), 10.0, 20.0, 0, 0, &redraw), MTK_STATUS_OK);
    EXPECT_EQ(redraw, 0);
    EXPECT_EQ(mtk_viewer_mouse_buttons(viewer.get(), 10.0, 20.0, MTK_MOUSE_BUTTON_LEFT, MTK_MODIFIER_KEY_SHIFT, &redraw),
              MTK_STATUS_OK);
    EXPECT_EQ(redraw, 0);
    EXPECT_EQ(mtk_viewer_scroll(viewer.get(), 10.0, 20.0, 1.5, &redraw), MTK_STATUS_OK);
    EXPECT_EQ(redraw, 0);
    EXPECT_EQ(mtk_viewer_mouse_leave(viewer.get(), &redraw), MTK_STATUS_OK);
    EXPECT_EQ(redraw, 0);

    int32_t animating = 1;
    EXPECT_EQ(mtk_viewer_redraw(viewer.get(), &animating), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    EXPECT_EQ(animating, 0);
}

TEST(Viewer, AcceptsColoursBetweenZeroAndOne) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);

    for (float valid : {0.0f, 0.5f, 1.0f}) {
        const mtk_viewer_colors value = colors(valid);
        EXPECT_EQ(mtk_viewer_set_colors(viewer.get(), &value), MTK_STATUS_OK) << valid;
    }
    for (float invalid : {-0.1f, 1.1f, std::numeric_limits<float>::quiet_NaN()}) {
        mtk_viewer_colors value = colors(0.5f);
        value.cube_text[1] = invalid;
        EXPECT_EQ(mtk_viewer_set_colors(viewer.get(), &value), MTK_STATUS_INVALID_ARGUMENT) << invalid;
        EXPECT_STRNE(mtk_viewer_last_error_message(), "");
    }
}

TEST(Viewer, TheViewCubeCannotBeInvisible) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    mtk_viewer_colors value = colors(0.5f);

    value.cube_transparency = 0.8f;
    EXPECT_EQ(mtk_viewer_set_colors(viewer.get(), &value), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    for (float invalid : {-0.1f, 1.0f, std::numeric_limits<float>::quiet_NaN()}) {
        value.cube_transparency = invalid;
        EXPECT_EQ(mtk_viewer_set_colors(viewer.get(), &value), MTK_STATUS_INVALID_ARGUMENT) << invalid;
    }
}

TEST(Viewer, RejectsPositionsAndDeltasThatAreNotFinite) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const double infinity = std::numeric_limits<double>::infinity();
    int32_t redraw = 1;

    EXPECT_EQ(mtk_viewer_mouse_move(viewer.get(), infinity, 0.0, 0, 0, &redraw), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(redraw, 0);
    EXPECT_EQ(mtk_viewer_mouse_buttons(viewer.get(), 0.0, std::numeric_limits<double>::quiet_NaN(), 0, 0, &redraw),
              MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_scroll(viewer.get(), 0.0, 0.0, infinity, &redraw), MTK_STATUS_INVALID_ARGUMENT);
}

TEST(Viewer, DescribesAViewNotShownYet) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const char* text = nullptr;

    ASSERT_EQ(mtk_viewer_get_diagnostics(viewer.get(), &text), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    ASSERT_NE(text, nullptr);
    EXPECT_NE(std::string(text).find("not attached"), std::string::npos) << text;
}

TEST(Viewer, SuccessfulCallClearsTheLastError) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    int32_t redraw = 0;
    ASSERT_EQ(mtk_viewer_scroll(viewer.get(), 0.0, 0.0, std::numeric_limits<double>::infinity(), &redraw),
              MTK_STATUS_INVALID_ARGUMENT);

    EXPECT_EQ(mtk_viewer_mouse_leave(viewer.get(), &redraw), MTK_STATUS_OK);
    EXPECT_STREQ(mtk_viewer_last_error_message(), "");
}

TEST(Viewer, NullArgumentsAreRejected) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    int32_t flag = 0;
    void* view = nullptr;
    const char* text = nullptr;

    EXPECT_EQ(mtk_viewer_create(parent.handle(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_get_native_view(nullptr, &view), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_get_native_view(viewer.get(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_colors(viewer.get(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_mouse_move(nullptr, 0.0, 0.0, 0, 0, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_mouse_move(viewer.get(), 0.0, 0.0, 0, 0, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_mouse_leave(nullptr, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_redraw(nullptr, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_redraw(viewer.get(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_get_diagnostics(nullptr, &text), MTK_STATUS_INVALID_ARGUMENT);
    mtk_viewer_release(nullptr);
}

#if defined(_WIN32)
TEST(Viewer, NeedsAParentWindowOnWindows) {
    mtk_viewer* viewer = nullptr;
    EXPECT_EQ(mtk_viewer_create(nullptr, &viewer), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(viewer, nullptr);
}
#endif

} // namespace
