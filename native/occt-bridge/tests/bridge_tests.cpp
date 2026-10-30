#include "mtk/occt_bridge.h"

#include <gtest/gtest.h>

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
    mtk_shape_release(nullptr);
}

} // namespace
