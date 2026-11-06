#include "internal.hpp"

#include <BRepBndLib.hxx>
#include <BRepGProp.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCone.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepPrimAPI_MakeTorus.hxx>
#include <Bnd_Box.hxx>
#include <GProp_GProps.hxx>
#include <TopLoc_Location.hxx>
#include <gp_Mat.hxx>
#include <gp_Trsf.hxx>

#include <algorithm>
#include <cmath>
#include <optional>

namespace {

bool is_positive_length(double value) {
    return std::isfinite(value) && value > 0.0;
}

// A rotation and a translation from the 12 values of the first three rows of a matrix; nothing for any other matrix.
std::optional<gp_Trsf> to_rigid_placement(const double* matrix) {
    for (int i = 0; i < 12; ++i) {
        if (!std::isfinite(matrix[i])) {
            return std::nullopt;
        }
    }

    // Orthonormal rows and columns with a positive determinant: no scaling, shearing or reflection.
    constexpr double tolerance = 1e-9;
    const gp_Mat linear(matrix[0], matrix[1], matrix[2], matrix[4], matrix[5], matrix[6], matrix[8], matrix[9],
                        matrix[10]);
    const gp_Mat product = linear.Multiplied(linear.Transposed());
    for (int row = 1; row <= 3; ++row) {
        for (int column = 1; column <= 3; ++column) {
            if (std::abs(product.Value(row, column) - (row == column ? 1.0 : 0.0)) > tolerance) {
                return std::nullopt;
            }
        }
    }
    if (linear.Determinant() < 0.0) {
        return std::nullopt;
    }

    gp_Trsf placement;
    placement.SetValues(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5], matrix[6], matrix[7],
                        matrix[8], matrix[9], matrix[10], matrix[11]);
    return placement;
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

mtk_status mtk_shape_make_cylinder(double radius, double height, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
        *out_shape = nullptr;
        if (!is_positive_length(radius) || !is_positive_length(height)) {
            return mtk::invalid_argument("Cylinder radius and height must be positive finite numbers.");
        }

        BRepPrimAPI_MakeCylinder maker(radius, height);
        *out_shape = new mtk_shape{maker.Shape()};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_make_sphere(double radius, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
        *out_shape = nullptr;
        if (!is_positive_length(radius)) {
            return mtk::invalid_argument("Sphere radius must be a positive finite number.");
        }

        BRepPrimAPI_MakeSphere maker(radius);
        *out_shape = new mtk_shape{maker.Shape()};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_make_cone(double bottom_radius, double top_radius, double height, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
        *out_shape = nullptr;
        const auto is_radius = [](double value) { return std::isfinite(value) && value >= 0.0; };
        if (!is_radius(bottom_radius) || !is_radius(top_radius) || !is_positive_length(height)) {
            return mtk::invalid_argument(
                "Cone radii must be finite numbers that are not negative, and its height positive and finite.");
        }
        if (bottom_radius + top_radius == 0.0) {
            return mtk::invalid_argument("The radii of a cone must not both be zero.");
        }
        if (bottom_radius == top_radius) {
            return mtk::invalid_argument("The radii of a cone must differ: equal radii make a cylinder.");
        }

        BRepPrimAPI_MakeCone maker(bottom_radius, top_radius, height);
        *out_shape = new mtk_shape{maker.Shape()};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_make_torus(double major_radius, double minor_radius, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
        *out_shape = nullptr;
        if (!is_positive_length(major_radius) || !is_positive_length(minor_radius)) {
            return mtk::invalid_argument("Torus radii must be positive finite numbers.");
        }
        if (minor_radius >= major_radius) {
            return mtk::invalid_argument("The minor radius of a torus must be smaller than its major radius.");
        }

        BRepPrimAPI_MakeTorus maker(major_radius, minor_radius);
        *out_shape = new mtk_shape{maker.Shape()};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_place(const mtk_shape* shape, const double* placement, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        if (shape == nullptr || placement == nullptr || out_shape == nullptr) {
            return mtk::invalid_argument("shape, placement and out_shape must not be null.");
        }
        *out_shape = nullptr;
        const std::optional<gp_Trsf> rigid = to_rigid_placement(placement);
        if (!rigid) {
            return mtk::invalid_argument(
                "A placement must be finite, a rotation and a translation, without scaling or reflection.");
        }

        *out_shape = new mtk_shape{shape->value.Moved(TopLoc_Location(*rigid))};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_bounds(const mtk_shape* shape, double* out_bounds) {
    return mtk::guarded([&] {
        if (shape == nullptr || out_bounds == nullptr) {
            return mtk::invalid_argument("shape and out_bounds must not be null.");
        }
        std::fill_n(out_bounds, 6, 0.0);

        // The tight box of curved surfaces, not the one of the control points, and without the tolerances of the shape.
        Bnd_Box box;
        BRepBndLib::AddOptimal(shape->value, box, false, false);
        if (box.IsVoid()) {
            return mtk::invalid_argument("The shape has no geometry.");
        }
        box.Get(out_bounds[0], out_bounds[1], out_bounds[2], out_bounds[3], out_bounds[4], out_bounds[5]);
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
