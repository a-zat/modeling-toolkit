#include "mtk/occt_bridge.h"

#include <gtest/gtest.h>

#include <array>
#include <cmath>
#include <limits>
#include <memory>

namespace {

struct ShapeDeleter {
    void operator()(mtk_shape* shape) const noexcept { mtk_shape_release(shape); }
};

using ShapePtr = std::unique_ptr<mtk_shape, ShapeDeleter>;

ShapePtr make_box(double dx, double dy, double dz) {
    mtk_shape* shape = nullptr;
    EXPECT_EQ(mtk_shape_make_box(dx, dy, dz, &shape), MTK_STATUS_OK) << mtk_last_error_message();
    return ShapePtr(shape);
}

double volume_of(const ShapePtr& shape) {
    double volume = 0.0;
    EXPECT_EQ(mtk_shape_volume(shape.get(), &volume), MTK_STATUS_OK) << mtk_last_error_message();
    return volume;
}

void expect_bounds(const ShapePtr& shape, const std::array<double, 6>& expected) {
    std::array<double, 6> actual{};
    ASSERT_EQ(mtk_shape_bounds(shape.get(), actual.data()), MTK_STATUS_OK) << mtk_last_error_message();
    for (size_t i = 0; i < 6; ++i) {
        EXPECT_NEAR(actual[i], expected[i], 1e-6) << "value " << i;
    }
}

ShapePtr place(const ShapePtr& shape, const std::array<double, 12>& placement) {
    mtk_shape* placed = nullptr;
    EXPECT_EQ(mtk_shape_place(shape.get(), placement.data(), &placed), MTK_STATUS_OK) << mtk_last_error_message();
    return ShapePtr(placed);
}

constexpr double pi = 3.14159265358979323846;
constexpr double nan = std::numeric_limits<double>::quiet_NaN();
constexpr double infinity = std::numeric_limits<double>::infinity();

TEST(Bridge, ReportsAbiAndOcctVersions) {
    EXPECT_EQ(mtk_bridge_abi_version(), MTK_OCCT_BRIDGE_ABI_VERSION);
    EXPECT_STRNE(mtk_occt_version(), "");
}

TEST(Shape, BoxVolumeIsTheProductOfItsDimensions) {
    ShapePtr box = make_box(10.0, 20.0, 30.0);
    double volume = 0.0;

    ASSERT_EQ(mtk_shape_volume(box.get(), &volume), MTK_STATUS_OK) << mtk_last_error_message();
    EXPECT_NEAR(volume, 6000.0, 1e-6);
}

TEST(Shape, MakeBoxRejectsInvalidDimensions) {
    const double invalid[] = {0.0, -1.0, std::numeric_limits<double>::quiet_NaN(),
                              std::numeric_limits<double>::infinity()};
    for (double value : invalid) {
        mtk_shape* shape = nullptr;
        EXPECT_EQ(mtk_shape_make_box(value, 1.0, 1.0, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
        EXPECT_EQ(shape, nullptr);
        EXPECT_STRNE(mtk_last_error_message(), "");
    }
}

TEST(Shape, PrimitivesHaveTheVolumesOfTheirFormulas) {
    mtk_shape* shape = nullptr;

    ASSERT_EQ(mtk_shape_make_cylinder(10.0, 40.0, &shape), MTK_STATUS_OK) << mtk_last_error_message();
    EXPECT_NEAR(volume_of(ShapePtr(shape)), pi * 10.0 * 10.0 * 40.0, 1e-3);

    ASSERT_EQ(mtk_shape_make_sphere(25.0, &shape), MTK_STATUS_OK) << mtk_last_error_message();
    EXPECT_NEAR(volume_of(ShapePtr(shape)), 4.0 / 3.0 * pi * 25.0 * 25.0 * 25.0, 1e-3);

    ASSERT_EQ(mtk_shape_make_cone(30.0, 10.0, 60.0, &shape), MTK_STATUS_OK) << mtk_last_error_message();
    EXPECT_NEAR(volume_of(ShapePtr(shape)), pi * 60.0 / 3.0 * (30.0 * 30.0 + 30.0 * 10.0 + 10.0 * 10.0), 1e-3);

    ASSERT_EQ(mtk_shape_make_cone(30.0, 0.0, 60.0, &shape), MTK_STATUS_OK) << mtk_last_error_message();
    EXPECT_NEAR(volume_of(ShapePtr(shape)), pi * 30.0 * 30.0 * 60.0 / 3.0, 1e-3);

    ASSERT_EQ(mtk_shape_make_torus(50.0, 10.0, &shape), MTK_STATUS_OK) << mtk_last_error_message();
    EXPECT_NEAR(volume_of(ShapePtr(shape)), 2.0 * pi * pi * 50.0 * 10.0 * 10.0, 1e-3);
}

TEST(Shape, PrimitivesStandWhereTheHeaderSaysTheyDo) {
    mtk_shape* shape = nullptr;

    expect_bounds(make_box(10.0, 20.0, 30.0), {0, 0, 0, 10, 20, 30});

    ASSERT_EQ(mtk_shape_make_cylinder(10.0, 40.0, &shape), MTK_STATUS_OK);
    expect_bounds(ShapePtr(shape), {-10, -10, 0, 10, 10, 40});

    ASSERT_EQ(mtk_shape_make_sphere(25.0, &shape), MTK_STATUS_OK);
    expect_bounds(ShapePtr(shape), {-25, -25, -25, 25, 25, 25});

    ASSERT_EQ(mtk_shape_make_cone(30.0, 10.0, 60.0, &shape), MTK_STATUS_OK);
    expect_bounds(ShapePtr(shape), {-30, -30, 0, 30, 30, 60});

    ASSERT_EQ(mtk_shape_make_torus(50.0, 10.0, &shape), MTK_STATUS_OK);
    expect_bounds(ShapePtr(shape), {-60, -60, -10, 60, 60, 10});
}

TEST(Shape, PlacementMovesAndTurnsAShapeWithoutChangingIt) {
    ShapePtr box = make_box(10.0, 20.0, 30.0);

    // A quarter turn about Z, then a translation: (x, y, z) -> (-y + 100, x + 200, z + 300).
    ShapePtr placed = place(box, {0, -1, 0, 100, 1, 0, 0, 200, 0, 0, 1, 300});

    expect_bounds(placed, {80, 200, 300, 100, 210, 330});
    EXPECT_NEAR(volume_of(placed), 6000.0, 1e-6);
    expect_bounds(box, {0, 0, 0, 10, 20, 30});

    // Placements compose: a shape that is already placed is moved from where it is.
    ShapePtr again = place(placed, {1, 0, 0, 5, 0, 1, 0, 0, 0, 0, 1, 0});
    expect_bounds(again, {85, 200, 300, 105, 210, 330});
}

TEST(Shape, PlacementRejectsWhatIsNotARotationAndATranslation) {
    ShapePtr box = make_box(1.0, 1.0, 1.0);
    const std::array<std::array<double, 12>, 6> invalid = {{
        {2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0},   // scaling
        {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0},  // reflection
        {1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0},   // shearing
        {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},   // nothing
        {1, 0, 0, nan, 0, 1, 0, 0, 0, 0, 1, 0}, // not a number
        {1, 0, 0, infinity, 0, 1, 0, 0, 0, 0, 1, 0},
    }};
    for (const std::array<double, 12>& placement : invalid) {
        mtk_shape* placed = nullptr;
        EXPECT_EQ(mtk_shape_place(box.get(), placement.data(), &placed), MTK_STATUS_INVALID_ARGUMENT);
        EXPECT_EQ(placed, nullptr);
        EXPECT_STRNE(mtk_last_error_message(), "");
    }
}

TEST(Shape, PrimitivesRejectInvalidDimensions) {
    mtk_shape* shape = nullptr;
    for (double value : {0.0, -1.0, nan, infinity}) {
        EXPECT_EQ(mtk_shape_make_cylinder(value, 1.0, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
        EXPECT_EQ(mtk_shape_make_cylinder(1.0, value, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
        EXPECT_EQ(mtk_shape_make_sphere(value, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
        EXPECT_EQ(mtk_shape_make_cone(1.0, 0.5, value, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
        EXPECT_EQ(mtk_shape_make_torus(value, 0.5, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
        EXPECT_EQ(mtk_shape_make_torus(2.0, value, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
        EXPECT_EQ(shape, nullptr);
    }
    for (double value : {-1.0, nan, infinity}) {
        EXPECT_EQ(mtk_shape_make_cone(value, 0.5, 1.0, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
        EXPECT_EQ(mtk_shape_make_cone(1.0, value, 1.0, &shape), MTK_STATUS_INVALID_ARGUMENT) << value;
    }
    EXPECT_EQ(mtk_shape_make_cone(0.0, 0.0, 1.0, &shape), MTK_STATUS_INVALID_ARGUMENT) << "radii of zero";
    EXPECT_EQ(mtk_shape_make_cone(2.0, 2.0, 1.0, &shape), MTK_STATUS_INVALID_ARGUMENT) << "equal radii";
    EXPECT_EQ(mtk_shape_make_torus(2.0, 2.0, &shape), MTK_STATUS_INVALID_ARGUMENT) << "equal radii";
    EXPECT_EQ(mtk_shape_make_torus(2.0, 3.0, &shape), MTK_STATUS_INVALID_ARGUMENT) << "minor radius too large";
    EXPECT_STRNE(mtk_last_error_message(), "");
}

TEST(Shape, SuccessfulCallClearsTheLastError) {
    mtk_shape* shape = nullptr;
    ASSERT_EQ(mtk_shape_make_box(-1.0, 1.0, 1.0, &shape), MTK_STATUS_INVALID_ARGUMENT);

    ShapePtr box = make_box(1.0, 1.0, 1.0);
    EXPECT_STREQ(mtk_last_error_message(), "");
}

TEST(Shape, NullArgumentsAreRejected) {
    double volume = 0.0;
    EXPECT_EQ(mtk_shape_volume(nullptr, &volume), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_make_box(1.0, 1.0, 1.0, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_make_cylinder(1.0, 1.0, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_make_sphere(1.0, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_make_cone(1.0, 0.5, 1.0, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_make_torus(2.0, 1.0, nullptr), MTK_STATUS_INVALID_ARGUMENT);

    ShapePtr box = make_box(1.0, 1.0, 1.0);
    const std::array<double, 12> identity = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};
    std::array<double, 6> bounds{};
    mtk_shape* placed = nullptr;
    EXPECT_EQ(mtk_shape_place(nullptr, identity.data(), &placed), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_place(box.get(), nullptr, &placed), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_place(box.get(), identity.data(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_bounds(nullptr, bounds.data()), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_bounds(box.get(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
    mtk_shape_release(nullptr);
}

} // namespace
