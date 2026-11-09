#include "internal.hpp"

#include <BRepAdaptor_Surface.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRep_Tool.hxx>
#include <BRepBndLib.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepGProp.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCone.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepPrimAPI_MakeTorus.hxx>
#include <Bnd_Box.hxx>
#include <GProp_GProps.hxx>
#include <TopExp.hxx>
#include <TopLoc_Location.hxx>
#include <TopTools_ShapeMapHasher.hxx>
#include <TopoDS.hxx>
#include <gp_Pln.hxx>
#include <gp_Mat.hxx>
#include <gp_Trsf.hxx>

#include <algorithm>
#include <array>
#include <cmath>
#include <limits>
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

// The face, edge or vertex of a shape with an index from 0, in the order of TopExp::MapShapes; null when the shape has
// no such element.
std::optional<TopoDS_Shape> element_of(const TopoDS_Shape& shape, TopAbs_ShapeEnum kind, int32_t index) {
    NCollection_IndexedMap<TopoDS_Shape, TopTools_ShapeMapHasher> elements;
    TopExp::MapShapes(shape, kind, elements);
    if (index < 0 || index >= elements.Extent()) {
        return std::nullopt;
    }
    return elements.FindKey(index + 1);
}

} // namespace

mtk_status mtk_shape_make_box(double dx, double dy, double dz, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_shape);
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
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
        mtk::clear_outputs(out_shape);
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
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
        mtk::clear_outputs(out_shape);
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
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
        mtk::clear_outputs(out_shape);
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
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
        mtk::clear_outputs(out_shape);
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
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
        mtk::clear_outputs(out_shape);
        if (shape == nullptr || placement == nullptr || out_shape == nullptr) {
            return mtk::invalid_argument("shape, placement and out_shape must not be null.");
        }
        const std::optional<gp_Trsf> rigid = to_rigid_placement(placement);
        if (!rigid) {
            return mtk::invalid_argument(
                "A placement must be finite, a rotation and a translation, without scaling or reflection.");
        }

        *out_shape = new mtk_shape{shape->value.Moved(TopLoc_Location(*rigid))};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_scale(const mtk_shape* shape, const double* centre, double factor, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_shape);
        if (shape == nullptr || centre == nullptr || out_shape == nullptr) {
            return mtk::invalid_argument("shape, centre and out_shape must not be null.");
        }
        if (!std::isfinite(factor) || factor <= 0 || !std::all_of(centre, centre + 3, [](double v) { return std::isfinite(v); })) {
            return mtk::invalid_argument("The factor must be positive and finite, and the centre finite.");
        }
        gp_Trsf scaling;
        scaling.SetScale(gp_Pnt(centre[0], centre[1], centre[2]), factor);
        BRepBuilderAPI_Transform scaled(shape->value, scaling, true);
        if (!scaled.IsDone()) {
            throw mtk::kernel_error("The shape could not be scaled.");
        }
        *out_shape = new mtk_shape{scaled.Shape()};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_bounds(const mtk_shape* shape, double* out_bounds) {
    return mtk::guarded([&] {
        if (out_bounds != nullptr) {
            std::fill_n(out_bounds, 6, 0.0);
        }
        if (shape == nullptr || out_bounds == nullptr) {
            return mtk::invalid_argument("shape and out_bounds must not be null.");
        }

        // The tight box of curved surfaces, not the one of the control points, and without the tolerances of the shape.
        Bnd_Box box;
        BRepBndLib::AddOptimal(shape->value, box, false, false);
        if (box.IsVoid()) {
            constexpr double infinity = std::numeric_limits<double>::infinity();
            std::fill_n(out_bounds, 3, infinity);
            std::fill_n(out_bounds + 3, 3, -infinity);
            return MTK_STATUS_OK;
        }
        box.Get(out_bounds[0], out_bounds[1], out_bounds[2], out_bounds[3], out_bounds[4], out_bounds[5]);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_volume(const mtk_shape* shape, double* out_volume) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_volume);
        if (shape == nullptr || out_volume == nullptr) {
            return mtk::invalid_argument("shape and out_volume must not be null.");
        }

        GProp_GProps properties;
        BRepGProp::VolumeProperties(shape->value, properties);
        *out_volume = properties.Mass();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_is_valid(const mtk_shape* shape, int32_t* out_valid) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_valid);
        if (shape == nullptr || out_valid == nullptr) {
            return mtk::invalid_argument("shape and out_valid must not be null.");
        }

        *out_valid = BRepCheck_Analyzer(shape->value).IsValid() ? 1 : 0;
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_count_elements(const mtk_shape* shape, int32_t* out_counts) {
    return mtk::guarded([&] {
        if (out_counts != nullptr) {
            std::fill_n(out_counts, 4, 0);
        }
        if (shape == nullptr || out_counts == nullptr) {
            return mtk::invalid_argument("shape and out_counts must not be null.");
        }

        int index = 0;
        for (const TopAbs_ShapeEnum kind : {TopAbs_SOLID, TopAbs_FACE, TopAbs_EDGE, TopAbs_VERTEX}) {
            NCollection_IndexedMap<TopoDS_Shape, TopTools_ShapeMapHasher> elements;
            TopExp::MapShapes(shape->value, kind, elements);
            out_counts[index++] = elements.Extent();
        }
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_face_info(const mtk_shape* shape, int32_t face, double* out_area, int32_t* out_planar,
                               double* out_origin, double* out_normal) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_area, out_planar);
        for (double* values : {out_origin, out_normal}) {
            if (values != nullptr) {
                std::fill_n(values, 3, 0.0);
            }
        }
        if (shape == nullptr || out_area == nullptr || out_planar == nullptr || out_origin == nullptr ||
            out_normal == nullptr) {
            return mtk::invalid_argument("shape, out_area, out_planar, out_origin and out_normal must not be null.");
        }
        const std::optional<TopoDS_Shape> element = element_of(shape->value, TopAbs_FACE, face);
        if (!element) {
            return mtk::invalid_argument("face is not a face of the shape.");
        }

        const TopoDS_Face& value = TopoDS::Face(*element);
        GProp_GProps properties;
        BRepGProp::SurfaceProperties(value, properties);
        *out_area = properties.Mass();

        // With the placement of the face; the normal of the surface turns with the orientation of the face.
        const BRepAdaptor_Surface surface(value);
        if (surface.GetType() == GeomAbs_Plane) {
            const gp_Pln plane = surface.Plane();
            gp_Dir normal = plane.Axis().Direction();
            if (value.Orientation() == TopAbs_REVERSED) {
                normal.Reverse();
            }
            const double distance = gp_Vec(normal).Dot(gp_Vec(plane.Location().XYZ()));
            const gp_XYZ origin = normal.XYZ() * distance;
            *out_planar = 1;
            std::copy_n(std::array{origin.X(), origin.Y(), origin.Z()}.data(), 3, out_origin);
            std::copy_n(std::array{normal.X(), normal.Y(), normal.Z()}.data(), 3, out_normal);
        }
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_shape_vertex_point(const mtk_shape* shape, int32_t vertex, double* out_point) {
    return mtk::guarded([&] {
        if (out_point != nullptr) {
            std::fill_n(out_point, 3, 0.0);
        }
        if (shape == nullptr || out_point == nullptr) {
            return mtk::invalid_argument("shape and out_point must not be null.");
        }
        const std::optional<TopoDS_Shape> element = element_of(shape->value, TopAbs_VERTEX, vertex);
        if (!element) {
            return mtk::invalid_argument("vertex is not a vertex of the shape.");
        }

        const gp_Pnt point = BRep_Tool::Pnt(TopoDS::Vertex(*element));
        out_point[0] = point.X();
        out_point[1] = point.Y();
        out_point[2] = point.Z();
        return MTK_STATUS_OK;
    });
}

void mtk_shape_release(mtk_shape* shape) {
    delete shape;
}
