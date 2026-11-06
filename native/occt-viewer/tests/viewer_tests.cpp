#include "mtk/occt_viewer.h"

#include <gtest/gtest.h>

#include <array>
#include <limits>
#include <memory>
#include <string>
#include <vector>

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
    for (float(*color)[3] : {&result.background_top, &result.background_bottom, &result.grid, &result.axis_x,
                             &result.axis_y, &result.axis_z, &result.cube_face, &result.cube_edge, &result.cube_text,
                             &result.highlight, &result.edges}) {
        for (float& component : *color) {
            component = value;
        }
    }
    return result;
}

// A square of two triangles on the ground, one metre wide, with normals and texture coordinates.
struct Square {
    std::array<double, 12> positions{0, 0, 0, 1000, 0, 0, 1000, 1000, 0, 0, 1000, 0};
    std::array<float, 12> normals{0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1};
    std::array<float, 8> uvs{0, 0, 1, 0, 1, 1, 0, 1};
    std::array<int32_t, 6> triangles{0, 1, 2, 0, 2, 3};

    mtk_viewer_mesh mesh() const {
        return {positions.data(), normals.data(), uvs.data(), 4, triangles.data(), 2, nullptr, nullptr, 0};
    }
};

constexpr double identity[12] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};

mtk_viewer_material grey(int32_t texture = -1) {
    return {{0.5f, 0.5f, 0.5f, 1.0f}, 0.0f, 0.8f, texture};
}

int32_t add_mesh(mtk_viewer* viewer, const mtk_viewer_mesh& mesh) {
    int32_t index = -1;
    EXPECT_EQ(mtk_viewer_add_mesh(viewer, &mesh, &index), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    return index;
}

int32_t add_material(mtk_viewer* viewer, const mtk_viewer_material& material) {
    int32_t index = -1;
    EXPECT_EQ(mtk_viewer_add_material(viewer, &material, &index), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    return index;
}

int32_t add_instance(mtk_viewer* viewer, int32_t mesh, int32_t material, const double* transform) {
    int32_t index = -1;
    EXPECT_EQ(mtk_viewer_add_instance(viewer, mesh, material, transform, &index), MTK_STATUS_OK)
        << mtk_viewer_last_error_message();
    return index;
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
    int32_t picked = 1, instance = 1;
    EXPECT_EQ(mtk_viewer_take_pick(viewer.get(), &picked, &instance), MTK_STATUS_OK);
    EXPECT_EQ(picked, 0) << "the click was not applied";
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

TEST(Viewer, ChangesHowItShowsTheScene) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const mtk_viewer_colors theme = colors(0.5f);
    ASSERT_EQ(mtk_viewer_set_colors(viewer.get(), &theme), MTK_STATUS_OK);

    // Without a scene, and with one: materials added later take the display mode too.
    for (int pass = 0; pass < 2; ++pass) {
        for (int32_t mode : {MTK_VIEWER_DISPLAY_WIREFRAME, MTK_VIEWER_DISPLAY_SHADED_WITH_EDGES,
                             MTK_VIEWER_DISPLAY_SHADED}) {
            EXPECT_EQ(mtk_viewer_set_display_mode(viewer.get(), mode), MTK_STATUS_OK)
                << mtk_viewer_last_error_message();
        }
        for (int32_t view = MTK_VIEWER_VIEW_TOP; view <= MTK_VIEWER_VIEW_ISOMETRIC; ++view) {
            EXPECT_EQ(mtk_viewer_show_view(viewer.get(), view), MTK_STATUS_OK) << mtk_viewer_last_error_message();
        }
        EXPECT_EQ(mtk_viewer_set_perspective(viewer.get(), 0), MTK_STATUS_OK);
        EXPECT_EQ(mtk_viewer_set_perspective(viewer.get(), 1), MTK_STATUS_OK);
        EXPECT_EQ(mtk_viewer_set_grid_visible(viewer.get(), 0), MTK_STATUS_OK);
        EXPECT_EQ(mtk_viewer_set_grid_visible(viewer.get(), 1), MTK_STATUS_OK);

        ASSERT_EQ(mtk_viewer_set_display_mode(viewer.get(), MTK_VIEWER_DISPLAY_WIREFRAME), MTK_STATUS_OK);
        add_mesh(viewer.get(), Square().mesh());
        add_material(viewer.get(), grey());
        add_instance(viewer.get(), 0, 0, identity);
    }

    EXPECT_EQ(mtk_viewer_show_view(viewer.get(), 7), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_show_view(viewer.get(), -1), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_display_mode(viewer.get(), 3), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_STRNE(mtk_viewer_last_error_message(), "");
}

TEST(Viewer, NumbersWhatItAddsToTheSceneFromZero) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const Square square;
    const std::vector<uint8_t> pixels(2 * 2 * 4, 255);
    int32_t texture = -1;

    EXPECT_EQ(add_mesh(viewer.get(), square.mesh()), 0);
    EXPECT_EQ(add_mesh(viewer.get(), square.mesh()), 1);
    ASSERT_EQ(mtk_viewer_add_texture(viewer.get(), pixels.data(), 2, 2, &texture), MTK_STATUS_OK)
        << mtk_viewer_last_error_message();
    EXPECT_EQ(texture, 0);
    EXPECT_EQ(add_material(viewer.get(), grey()), 0);
    EXPECT_EQ(add_material(viewer.get(), grey(texture)), 1);
    EXPECT_EQ(add_instance(viewer.get(), 1, 1, identity), 0);
    EXPECT_EQ(add_instance(viewer.get(), 0, 1, identity), 1);
    EXPECT_EQ(mtk_viewer_fit_all(viewer.get()), MTK_STATUS_OK);

    ASSERT_EQ(mtk_viewer_clear(viewer.get()), MTK_STATUS_OK);
    EXPECT_EQ(add_mesh(viewer.get(), square.mesh()), 0);
    int32_t instance = -1;
    EXPECT_EQ(mtk_viewer_add_instance(viewer.get(), 0, 0, identity, &instance), MTK_STATUS_INVALID_ARGUMENT)
        << "the materials were cleared";
    EXPECT_EQ(instance, 0);
    EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), &instance, 1), MTK_STATUS_INVALID_ARGUMENT)
        << "the instances were cleared";
}

TEST(Viewer, ShadesMeshesWithoutNormalsFlat) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    Square square;
    mtk_viewer_mesh mesh = square.mesh();
    mesh.normals = nullptr;
    mesh.uvs = nullptr;

    const int32_t index = add_mesh(viewer.get(), mesh);
    const int32_t material = add_material(viewer.get(), grey());
    EXPECT_EQ(add_instance(viewer.get(), index, material, identity), 0);
}

TEST(Viewer, PlacesInstancesWithAnyInvertibleTransform) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const int32_t mesh = add_mesh(viewer.get(), Square().mesh());
    const int32_t material = add_material(viewer.get(), grey());
    // Rotated a quarter turn, scaled and moved; mirrored; stretched along X, which needs a copy of the triangles.
    const double rotated[12] = {0, -2, 0, 100, 2, 0, 0, 200, 0, 0, 2, 300};
    const double mirrored[12] = {-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};
    const double stretched[12] = {3, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};

    for (const double* transform : {rotated, mirrored, stretched}) {
        add_instance(viewer.get(), mesh, material, transform);
    }
}

// The square with edges of its own: its outline, without the diagonal between its triangles.
struct OutlinedSquare : Square {
    std::array<double, 15> edge_points{0, 0, 0, 1000, 0, 0, 1000, 1000, 0, 0, 1000, 0, 0, 0, 0};
    std::array<int32_t, 2> edge_first_points{0, 5};

    mtk_viewer_mesh mesh() const {
        mtk_viewer_mesh result = Square::mesh();
        result.edge_points = edge_points.data();
        result.edge_first_points = edge_first_points.data();
        result.edge_count = 1;
        return result;
    }
};

TEST(Viewer, ShowsMeshesWithEdgesOfTheirOwnInEveryDisplayMode) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const int32_t outlined = add_mesh(viewer.get(), OutlinedSquare().mesh());
    mtk_viewer_mesh without_edges = Square().mesh();
    const int32_t none = 0;
    without_edges.edge_first_points = &none; // edges of its own, but none: not those of the triangles either
    const int32_t bare = add_mesh(viewer.get(), without_edges);
    const int32_t material = add_material(viewer.get(), grey());
    const double stretched[12] = {3, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};
    const int32_t first = add_instance(viewer.get(), outlined, material, identity);
    add_instance(viewer.get(), outlined, material, stretched);
    add_instance(viewer.get(), bare, material, identity);

    for (int32_t mode : {MTK_VIEWER_DISPLAY_WIREFRAME, MTK_VIEWER_DISPLAY_SHADED_WITH_EDGES,
                         MTK_VIEWER_DISPLAY_SHADED}) {
        EXPECT_EQ(mtk_viewer_set_display_mode(viewer.get(), mode), MTK_STATUS_OK) << mtk_viewer_last_error_message();
        EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), &first, 1), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    }
}

TEST(Viewer, RejectsEdgesItCannotShow) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    int32_t index = -1;

    OutlinedSquare not_from_zero;
    not_from_zero.edge_first_points = {1, 5};
    OutlinedSquare one_point;
    one_point.edge_first_points = {0, 1};
    OutlinedSquare infinite;
    infinite.edge_points[7] = std::numeric_limits<double>::infinity();
    mtk_viewer_mesh without_points = OutlinedSquare().mesh();
    without_points.edge_points = nullptr;
    mtk_viewer_mesh negative = OutlinedSquare().mesh();
    negative.edge_count = -1;

    for (const mtk_viewer_mesh& mesh :
         {not_from_zero.mesh(), one_point.mesh(), infinite.mesh(), without_points, negative}) {
        EXPECT_EQ(mtk_viewer_add_mesh(viewer.get(), &mesh, &index), MTK_STATUS_INVALID_ARGUMENT);
        EXPECT_STRNE(mtk_viewer_last_error_message(), "");
    }
}

TEST(Viewer, SelectsAnyOfItsInstances) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const int32_t mesh = add_mesh(viewer.get(), Square().mesh());
    const int32_t material = add_material(viewer.get(), grey());
    // A connected instance and one with triangles of its own, both faded and shaded again.
    const double stretched[12] = {3, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};
    const int32_t first = add_instance(viewer.get(), mesh, material, identity);
    const int32_t second = add_instance(viewer.get(), mesh, material, stretched);
    const int32_t third = add_instance(viewer.get(), mesh, material, identity);

    const std::vector<std::vector<int32_t>> selections = {
        {first}, {second, third}, {third, third}, {first, second, third}, {}, {second}};
    for (const std::vector<int32_t>& selection : selections) {
        EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), selection.data(), static_cast<int32_t>(selection.size())),
                  MTK_STATUS_OK)
            << mtk_viewer_last_error_message();
    }
    EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), nullptr, 0), MTK_STATUS_OK);
}

TEST(Viewer, ChangesAndRemovesInstancesKeepingTheIndicesOfTheOthers) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const Square square;
    const int32_t first_mesh = add_mesh(viewer.get(), square.mesh());
    const int32_t second_mesh = add_mesh(viewer.get(), square.mesh());
    const int32_t material = add_material(viewer.get(), grey());
    const int32_t other_material = add_material(viewer.get(), grey());
    const double stretched[12] = {3, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};
    const int32_t first = add_instance(viewer.get(), first_mesh, material, identity);
    const int32_t second = add_instance(viewer.get(), first_mesh, material, identity);
    const int32_t third = add_instance(viewer.get(), first_mesh, material, identity);
    ASSERT_EQ(mtk_viewer_set_selection(viewer.get(), &first, 1), MTK_STATUS_OK) << mtk_viewer_last_error_message();

    // Faded and shown again, connected and with triangles of its own.
    EXPECT_EQ(mtk_viewer_set_instance(viewer.get(), second, second_mesh, other_material, stretched), MTK_STATUS_OK)
        << mtk_viewer_last_error_message();
    EXPECT_EQ(mtk_viewer_set_instance(viewer.get(), first, second_mesh, material, identity), MTK_STATUS_OK)
        << mtk_viewer_last_error_message();
    EXPECT_EQ(mtk_viewer_remove_mesh(viewer.get(), first_mesh), MTK_STATUS_INVALID_ARGUMENT)
        << "the third instance places it";
    EXPECT_EQ(mtk_viewer_remove_instance(viewer.get(), third), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    EXPECT_EQ(mtk_viewer_remove_mesh(viewer.get(), first_mesh), MTK_STATUS_OK) << mtk_viewer_last_error_message();

    EXPECT_EQ(add_instance(viewer.get(), second_mesh, material, identity), 3) << "indices are not given again";
    const int32_t both[2] = {first, second};
    EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), both, 2), MTK_STATUS_OK) << mtk_viewer_last_error_message();
    EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), &third, 1), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_remove_instance(viewer.get(), third), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_instance(viewer.get(), third, second_mesh, material, identity),
              MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_instance(viewer.get(), first, first_mesh, material, identity),
              MTK_STATUS_INVALID_ARGUMENT)
        << "the mesh was removed";
    int32_t instance = -1;
    EXPECT_EQ(mtk_viewer_add_instance(viewer.get(), first_mesh, material, identity, &instance),
              MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_remove_mesh(viewer.get(), first_mesh), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_fit_all(viewer.get()), MTK_STATUS_OK);
    EXPECT_EQ(mtk_viewer_set_display_mode(viewer.get(), MTK_VIEWER_DISPLAY_SHADED_WITH_EDGES), MTK_STATUS_OK);
    EXPECT_EQ(mtk_viewer_clear(viewer.get()), MTK_STATUS_OK);
}

TEST(Viewer, RejectsChangesOfInstancesItCannotPlace) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const int32_t mesh = add_mesh(viewer.get(), Square().mesh());
    const int32_t material = add_material(viewer.get(), grey());
    const int32_t instance = add_instance(viewer.get(), mesh, material, identity);
    const double flat[12] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0};

    EXPECT_EQ(mtk_viewer_set_instance(viewer.get(), instance, mesh, material, flat), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_instance(viewer.get(), instance, mesh, 1, identity), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_instance(viewer.get(), instance, -1, material, identity), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_instance(viewer.get(), instance, mesh, material, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_instance(nullptr, instance, mesh, material, identity), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_remove_instance(nullptr, instance), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_remove_mesh(nullptr, mesh), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_remove_mesh(viewer.get(), 5), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_STRNE(mtk_viewer_last_error_message(), "");
}

TEST(Viewer, RejectsMeshesItCannotShow) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    Square square;
    int32_t index = -1;

    mtk_viewer_mesh empty = square.mesh();
    empty.triangle_count = 0;
    mtk_viewer_mesh without_positions = square.mesh();
    without_positions.positions = nullptr;
    Square outside;
    outside.triangles[5] = 4;
    Square infinite;
    infinite.positions[4] = std::numeric_limits<double>::infinity();

    for (const mtk_viewer_mesh& mesh : {empty, without_positions, outside.mesh(), infinite.mesh()}) {
        EXPECT_EQ(mtk_viewer_add_mesh(viewer.get(), &mesh, &index), MTK_STATUS_INVALID_ARGUMENT);
        EXPECT_STRNE(mtk_viewer_last_error_message(), "");
    }
}

TEST(Viewer, RejectsMaterialsAndTexturesItCannotShow) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const uint8_t pixel[4] = {255, 0, 0, 255};
    int32_t index = -1;

    mtk_viewer_material bright = grey();
    bright.base_color[0] = 1.5f;
    mtk_viewer_material undefined = grey();
    undefined.roughness = std::numeric_limits<float>::quiet_NaN();
    const mtk_viewer_material untextured = grey(0); // no texture was added
    for (const mtk_viewer_material& material : {bright, undefined, untextured}) {
        EXPECT_EQ(mtk_viewer_add_material(viewer.get(), &material, &index), MTK_STATUS_INVALID_ARGUMENT);
    }
    EXPECT_EQ(mtk_viewer_add_texture(viewer.get(), pixel, 0, 1, &index), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_texture(viewer.get(), pixel, 1, 20000, &index), MTK_STATUS_INVALID_ARGUMENT);
}

TEST(Viewer, RejectsInstancesItCannotPlace) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const int32_t mesh = add_mesh(viewer.get(), Square().mesh());
    const int32_t material = add_material(viewer.get(), grey());
    const double flat[12] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0};
    double undefined[12] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};
    undefined[3] = std::numeric_limits<double>::quiet_NaN();

    int32_t index = -1;

    EXPECT_EQ(mtk_viewer_add_instance(viewer.get(), mesh + 1, material, identity, &index), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_instance(viewer.get(), mesh, material + 1, identity, &index), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_instance(viewer.get(), mesh, material, nullptr, &index), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_instance(viewer.get(), mesh, material, flat, &index), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_instance(viewer.get(), mesh, material, undefined, &index), MTK_STATUS_INVALID_ARGUMENT);
}

TEST(Viewer, RejectsSelectionsOfInstancesItDoesNotHave) {
    Parent parent;
    ViewerPtr viewer = create_viewer(parent);
    const int32_t mesh = add_mesh(viewer.get(), Square().mesh());
    const int32_t instance = add_instance(viewer.get(), mesh, add_material(viewer.get(), grey()), identity);

    for (int32_t missing : {instance + 1, -1}) {
        EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), &missing, 1), MTK_STATUS_INVALID_ARGUMENT) << missing;
        EXPECT_STRNE(mtk_viewer_last_error_message(), "");
    }
    EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), &instance, -1), MTK_STATUS_INVALID_ARGUMENT);
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
    const mtk_viewer_mesh mesh = Square().mesh();
    const mtk_viewer_material material = grey();
    const uint8_t pixel[4] = {};
    EXPECT_EQ(mtk_viewer_add_mesh(viewer.get(), nullptr, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_mesh(viewer.get(), &mesh, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_texture(viewer.get(), nullptr, 1, 1, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_texture(viewer.get(), pixel, 1, 1, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_material(viewer.get(), nullptr, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_material(viewer.get(), &material, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_instance(nullptr, 0, 0, identity, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_add_instance(viewer.get(), 0, 0, identity, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_selection(nullptr, nullptr, 0), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_selection(viewer.get(), nullptr, 1), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_take_pick(nullptr, &flag, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_take_pick(viewer.get(), nullptr, &flag), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_take_pick(viewer.get(), &flag, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_clear(nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_fit_all(nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_show_view(nullptr, MTK_VIEWER_VIEW_TOP), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_display_mode(nullptr, MTK_VIEWER_DISPLAY_SHADED), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_perspective(nullptr, 1), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_viewer_set_grid_visible(nullptr, 1), MTK_STATUS_INVALID_ARGUMENT);
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
