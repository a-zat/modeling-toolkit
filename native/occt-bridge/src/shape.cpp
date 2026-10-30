#include "internal.hpp"

#include <BRepGProp.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <GProp_GProps.hxx>

#include <cmath>

namespace {

bool is_positive_length(double value) {
    return std::isfinite(value) && value > 0.0;
}

} // namespace

mtk_status mtk_shape_make_box(double dx, double dy, double dz, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
        *out_shape = nullptr;
        if (!is_positive_length(dx) || !is_positive_length(dy) || !is_positive_length(dz)) {
            return mtk::invalid_argument("Box dimensions must be positive finite numbers.");
        }

        BRepPrimAPI_MakeBox maker(dx, dy, dz);
        *out_shape = new mtk_shape{maker.Shape()};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_volume(const mtk_shape* shape, double* out_volume) {
    return mtk::guarded([&] {
        if (shape == nullptr || out_volume == nullptr) {
            return mtk::invalid_argument("shape and out_volume must not be null.");
        }
        *out_volume = 0.0;

        GProp_GProps properties;
        BRepGProp::VolumeProperties(shape->value, properties);
        *out_volume = properties.Mass();
        return MTK_STATUS_OK;
    });
}

void mtk_shape_release(mtk_shape* shape) {
    delete shape;
}
