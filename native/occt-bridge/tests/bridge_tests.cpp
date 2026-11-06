#include "mtk/occt_bridge.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <limits>
#include <memory>
#include <string>
#include <thread>
#include <vector>

namespace {

struct ShapeDeleter {
    void operator()(mtk_shape* shape) const noexcept { mtk_shape_release(shape); }
};

using ShapePtr = std::unique_ptr<mtk_shape, ShapeDeleter>;

struct TessellationDeleter {
    void operator()(mtk_tessellation* tessellation) const noexcept { mtk_tessellation_release(tessellation); }
};

using TessellationPtr = std::unique_ptr<mtk_tessellation, TessellationDeleter>;

struct BytesDeleter {
    void operator()(mtk_bytes* bytes) const noexcept { mtk_bytes_release(bytes); }
};

using BytesPtr = std::unique_ptr<mtk_bytes, BytesDeleter>;

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

TessellationPtr tessellate(const ShapePtr& shape, double linear_deflection, double angular_deflection = 0.5) {
    mtk_tessellation* tessellation = nullptr;
    EXPECT_EQ(mtk_shape_tessellate(shape.get(), linear_deflection, angular_deflection, nullptr, &tessellation),
              MTK_STATUS_OK)
        << mtk_last_error_message();
    return TessellationPtr(tessellation);
}

mtk_mesh_info mesh_of(const TessellationPtr& tessellation) {
    mtk_mesh_info mesh{};
    EXPECT_EQ(mtk_tessellation_get_mesh(tessellation.get(), &mesh), MTK_STATUS_OK) << mtk_last_error_message();
    return mesh;
}

// The triangles of each face, as ranges [first, next first).
std::vector<int32_t> faces_of(const TessellationPtr& tessellation) {
    const int32_t* first_triangles = nullptr;
    int32_t count = -1;
    EXPECT_EQ(mtk_tessellation_get_faces(tessellation.get(), &first_triangles, &count), MTK_STATUS_OK)
        << mtk_last_error_message();
    return std::vector<int32_t>(first_triangles, first_triangles + count + 1);
}

using Polyline = std::vector<std::array<double, 3>>;

std::vector<Polyline> edges_of(const TessellationPtr& tessellation) {
    const double* points = nullptr;
    const int32_t* first_points = nullptr;
    int32_t count = -1;
    EXPECT_EQ(mtk_tessellation_get_edges(tessellation.get(), &points, &first_points, &count), MTK_STATUS_OK)
        << mtk_last_error_message();
    std::vector<Polyline> edges;
    for (int32_t edge = 0; edge < count; ++edge) {
        Polyline polyline;
        for (int32_t point = first_points[edge]; point < first_points[edge + 1]; ++point) {
            polyline.push_back({points[3 * point], points[3 * point + 1], points[3 * point + 2]});
        }
        edges.push_back(polyline);
    }
    return edges;
}

// What a mesh encloses and how large its surface is, from its triangles, and whether the front of every triangle, from
// where its vertices run counter-clockwise, is on the side of the normals of its vertices (leaving out triangles of
// no area).
struct MeshMeasures {
    double area = 0.0;
    double volume = 0.0; // negative when the triangles face inwards
    int triangles_against_their_normals = 0;
    int vertices_with_normals_of_any_other_length = 0;
    std::array<double, 6> bounds{};
};

MeshMeasures measure(const mtk_mesh_info& mesh) {
    MeshMeasures measures;
    measures.bounds = {1e300, 1e300, 1e300, -1e300, -1e300, -1e300};
    const auto vertex = [&](int index) {
        return std::array<double, 3>{mesh.vertices[3 * index], mesh.vertices[3 * index + 1],
                                     mesh.vertices[3 * index + 2]};
    };
    const auto cross = [](const std::array<double, 3>& a, const std::array<double, 3>& b) {
        return std::array<double, 3>{a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]};
    };
    const auto dot = [](const std::array<double, 3>& a, const std::array<double, 3>& b) {
        return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    };

    for (int index = 0; index < mesh.vertex_count; ++index) {
        const std::array<double, 3> point = vertex(index);
        for (int axis = 0; axis < 3; ++axis) {
            measures.bounds[axis] = std::min(measures.bounds[axis], point[axis]);
            measures.bounds[3 + axis] = std::max(measures.bounds[3 + axis], point[axis]);
        }
        const double length = std::hypot(mesh.normals[3 * index], mesh.normals[3 * index + 1],
                                         mesh.normals[3 * index + 2]);
        if (std::abs(length - 1.0) > 1e-4) {
            ++measures.vertices_with_normals_of_any_other_length;
        }
    }
    for (int triangle = 0; triangle < mesh.triangle_count; ++triangle) {
        const int a = mesh.triangles[3 * triangle];
        const int b = mesh.triangles[3 * triangle + 1];
        const int c = mesh.triangles[3 * triangle + 2];
        const std::array<double, 3> pa = vertex(a);
        const std::array<double, 3> pb = vertex(b);
        const std::array<double, 3> pc = vertex(c);
        const std::array<double, 3> ab = {pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]};
        const std::array<double, 3> ac = {pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2]};
        const std::array<double, 3> facing = cross(ab, ac);
        measures.area += 0.5 * std::sqrt(dot(facing, facing));
        measures.volume += dot(pa, cross(pb, pc)) / 6.0;

        double along_normals = 0.0;
        for (const int index : {a, b, c}) {
            along_normals += facing[0] * mesh.normals[3 * index] + facing[1] * mesh.normals[3 * index + 1] +
                             facing[2] * mesh.normals[3 * index + 2];
        }
        // The poles of a sphere leave triangles of no area, whose front is not defined.
        constexpr double degenerate_area = 1e-9;
        if (along_normals <= 0.0 && 0.5 * std::sqrt(dot(facing, facing)) > degenerate_area) {
            ++measures.triangles_against_their_normals;
        }
    }
    return measures;
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

int32_t is_valid(const mtk_shape* shape) {
    int32_t valid = -1;
    EXPECT_EQ(mtk_shape_is_valid(shape, &valid), MTK_STATUS_OK) << mtk_last_error_message();
    return valid;
}

TEST(Shape, TellsAValidShapeFromAnInvalidOne) {
    ShapePtr box = make_box(10.0, 20.0, 30.0);
    EXPECT_EQ(is_valid(box.get()), 1);

    // The same box with a corner moved away from the edges that end there.
    mtk_bytes* bytes = nullptr;
    ASSERT_EQ(mtk_shape_write(box.get(), &bytes), MTK_STATUS_OK) << mtk_last_error_message();
    const uint8_t* data = nullptr;
    int64_t size = 0;
    ASSERT_EQ(mtk_bytes_get(bytes, &data, &size), MTK_STATUS_OK);
    std::string text(reinterpret_cast<const char*>(data), static_cast<size_t>(size));
    mtk_bytes_release(bytes);
    const size_t vertex = text.find("\nVe\n");
    ASSERT_NE(vertex, std::string::npos);
    const size_t point = text.find('\n', vertex + 4) + 1; // after the tolerance
    text.replace(point, text.find('\n', point) - point, "5 5 5");
    mtk_shape* moved = nullptr;
    ASSERT_EQ(mtk_shape_read(reinterpret_cast<const uint8_t*>(text.data()), static_cast<int64_t>(text.size()), &moved),
              MTK_STATUS_OK)
        << mtk_last_error_message();
    EXPECT_EQ(is_valid(ShapePtr(moved).get()), 0);

    int32_t valid = 0;
    EXPECT_EQ(mtk_shape_is_valid(nullptr, &valid), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_is_valid(box.get(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
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

std::string written(const ShapePtr& shape) {
    mtk_bytes* bytes = nullptr;
    EXPECT_EQ(mtk_shape_write(shape.get(), &bytes), MTK_STATUS_OK) << mtk_last_error_message();
    BytesPtr owner(bytes);
    const uint8_t* data = nullptr;
    int64_t size = 0;
    EXPECT_EQ(mtk_bytes_get(bytes, &data, &size), MTK_STATUS_OK) << mtk_last_error_message();
    return std::string(reinterpret_cast<const char*>(data), static_cast<size_t>(size));
}

// A compound with nothing in it, in the BRep format.
constexpr const char* empty_compound = "CASCADE Topology V3, (c) Open Cascade\n"
                                       "Locations 0\nCurve2ds 0\nCurves 0\nPolygon3D 0\nPolygonOnTriangulations 0\n"
                                       "Surfaces 0\nTriangulations 0\n\nTShapes 1\nCo\n\n1100000\n*\n\n+1 0\n";

ShapePtr read_shape(const std::string& data) {
    mtk_shape* shape = nullptr;
    EXPECT_EQ(mtk_shape_read(reinterpret_cast<const uint8_t*>(data.data()), static_cast<int64_t>(data.size()), &shape),
              MTK_STATUS_OK)
        << mtk_last_error_message();
    return ShapePtr(shape);
}

ShapePtr combine(const ShapePtr& first, const ShapePtr& second, mtk_boolean_operation operation) {
    mtk_shape* result = nullptr;
    EXPECT_EQ(mtk_shape_boolean(first.get(), second.get(), operation, nullptr, &result), MTK_STATUS_OK)
        << mtk_last_error_message();
    return ShapePtr(result);
}

TEST(Boolean, CombinesOverlappingBoxesIntoTheVolumesOfSetOperations) {
    ShapePtr a = make_box(10.0, 10.0, 10.0);
    ShapePtr b = place(make_box(10.0, 10.0, 10.0), {1, 0, 0, 5, 0, 1, 0, 0, 0, 0, 1, 0}); // [5, 15] x [0, 10] x [0, 10]

    ShapePtr both = combine(a, b, MTK_BOOLEAN_UNION);
    ShapePtr common = combine(a, b, MTK_BOOLEAN_INTERSECTION);
    ShapePtr only_a = combine(a, b, MTK_BOOLEAN_DIFFERENCE);

    EXPECT_NEAR(volume_of(both), 1500.0, 1e-6);
    EXPECT_NEAR(volume_of(common), 500.0, 1e-6);
    EXPECT_NEAR(volume_of(only_a), 500.0, 1e-6);
    expect_bounds(both, {0, 0, 0, 15, 10, 10});
    expect_bounds(common, {5, 0, 0, 10, 10, 10});
    expect_bounds(only_a, {0, 0, 0, 5, 10, 10});
    EXPECT_NEAR(volume_of(a), 1000.0, 1e-6) << "the operands are not changed";
}

TEST(Boolean, DrillsAHoleThroughABox) {
    ShapePtr box = make_box(100.0, 100.0, 100.0);
    mtk_shape* shape = nullptr;
    ASSERT_EQ(mtk_shape_make_cylinder(20.0, 120.0, &shape), MTK_STATUS_OK);
    ShapePtr drill = place(ShapePtr(shape), {1, 0, 0, 50, 0, 1, 0, 50, 0, 0, 1, -10});

    ShapePtr drilled = combine(box, drill, MTK_BOOLEAN_DIFFERENCE);

    EXPECT_NEAR(volume_of(drilled), 1e6 - pi * 20.0 * 20.0 * 100.0, 1e-2);
    expect_bounds(drilled, {0, 0, 0, 100, 100, 100});
}

TEST(Boolean, UnitesShapesThatDoNotMeet) {
    ShapePtr a = make_box(10.0, 10.0, 10.0);
    ShapePtr b = place(make_box(10.0, 10.0, 10.0), {1, 0, 0, 100, 0, 1, 0, 0, 0, 0, 1, 0});

    ShapePtr both = combine(a, b, MTK_BOOLEAN_UNION);

    EXPECT_NEAR(volume_of(both), 2000.0, 1e-6);
    expect_bounds(both, {0, 0, 0, 110, 10, 10});
}

TEST(Boolean, MergesTheFacesAndEdgesThatTheOperationSplits) {
    // Two boxes side by side make one box, of six faces and twelve edges.
    ShapePtr left = make_box(10.0, 10.0, 10.0);
    ShapePtr right = place(make_box(10.0, 10.0, 10.0), {1, 0, 0, 10, 0, 1, 0, 0, 0, 0, 1, 0});
    mtk_shape* made = nullptr;
    ASSERT_EQ(mtk_shape_boolean(left.get(), right.get(), MTK_BOOLEAN_UNION, nullptr, &made), MTK_STATUS_OK)
        << mtk_last_error_message();
    ShapePtr united(made);

    TessellationPtr tessellation = tessellate(united, 1.0);
    EXPECT_EQ(faces_of(tessellation).size(), 7u);
    EXPECT_EQ(edges_of(tessellation).size(), 12u);
    EXPECT_NEAR(volume_of(united), 2000.0, 1e-6);
}

TEST(Boolean, ReportsAnEmptyResultAsAnError) {
    ShapePtr a = make_box(10.0, 10.0, 10.0);
    ShapePtr far = place(make_box(10.0, 10.0, 10.0), {1, 0, 0, 100, 0, 1, 0, 0, 0, 0, 1, 0});
    mtk_shape* result = nullptr;

    EXPECT_EQ(mtk_shape_boolean(a.get(), far.get(), MTK_BOOLEAN_INTERSECTION, nullptr, &result), MTK_STATUS_KERNEL_ERROR);
    EXPECT_EQ(result, nullptr);
    EXPECT_NE(std::string(mtk_last_error_message()).find("empty"), std::string::npos);

    EXPECT_EQ(mtk_shape_boolean(a.get(), a.get(), MTK_BOOLEAN_DIFFERENCE, nullptr, &result), MTK_STATUS_KERNEL_ERROR);
    EXPECT_EQ(result, nullptr);
    EXPECT_NE(std::string(mtk_last_error_message()).find("empty"), std::string::npos);
}

TEST(Boolean, RejectsInvalidArguments) {
    ShapePtr box = make_box(1.0, 1.0, 1.0);
    mtk_shape* result = nullptr;

    EXPECT_EQ(mtk_shape_boolean(nullptr, box.get(), MTK_BOOLEAN_UNION, nullptr, &result), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_boolean(box.get(), nullptr, MTK_BOOLEAN_UNION, nullptr, &result), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_boolean(box.get(), box.get(), MTK_BOOLEAN_UNION, nullptr, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    for (int32_t operation : {-1, 3}) {
        EXPECT_EQ(mtk_shape_boolean(box.get(), box.get(), operation, nullptr, &result), MTK_STATUS_INVALID_ARGUMENT)
            << operation;
        EXPECT_EQ(result, nullptr);
        EXPECT_STRNE(mtk_last_error_message(), "");
    }
}

TEST(Serialization, ASolidSurvivesAWriteAndARead) {
    mtk_shape* shape = nullptr;
    ASSERT_EQ(mtk_shape_make_cylinder(10.0, 40.0, &shape), MTK_STATUS_OK);
    ShapePtr cylinder = place(ShapePtr(shape), {0, -1, 0, 100, 1, 0, 0, 200, 0, 0, 1, 300});

    const std::string data = written(cylinder);
    ShapePtr copy = read_shape(data);

    EXPECT_FALSE(data.empty());
    EXPECT_NEAR(volume_of(copy), pi * 10.0 * 10.0 * 40.0, 1e-3);
    expect_bounds(copy, {90, 190, 300, 110, 210, 340});
}

TEST(Serialization, LeavesOutTheTrianglesOfAShape) {
    ShapePtr box = make_box(10.0, 20.0, 30.0);
    ASSERT_NE(tessellate(box, 1.0), nullptr);

    const std::string data = written(box);

    EXPECT_NE(data.find("\nTriangulations 0\n"), std::string::npos);
}

TEST(Serialization, AnEmptyCompoundIsAShapeWithAnEmptyBox) {
    const ShapePtr empty = read_shape(empty_compound);
    ASSERT_NE(empty, nullptr);

    double bounds[6] = {};
    ASSERT_EQ(mtk_shape_bounds(empty.get(), bounds), MTK_STATUS_OK) << mtk_last_error_message();
    for (int axis = 0; axis < 3; ++axis) {
        EXPECT_GT(bounds[axis], bounds[axis + 3]);
    }

    mtk_tessellation* tessellation = nullptr;
    EXPECT_EQ(mtk_shape_tessellate(empty.get(), 1.0, 0.5, nullptr, &tessellation), MTK_STATUS_INVALID_ARGUMENT);
}

TEST(Serialization, RejectsDataThatIsNotAShape) {
    mtk_shape* shape = nullptr;
    const std::string text = "this is not a shape";
    const std::string truncated = written(make_box(1.0, 1.0, 1.0)).substr(0, 40);
    for (const std::string& data : {text, truncated, std::string(64, '\0'), std::string("DBRep_DrawableShape\n")}) {
        EXPECT_EQ(mtk_shape_read(reinterpret_cast<const uint8_t*>(data.data()), static_cast<int64_t>(data.size()),
                                 &shape),
                  MTK_STATUS_INVALID_ARGUMENT)
            << data.substr(0, 20);
        EXPECT_EQ(shape, nullptr);
        EXPECT_STRNE(mtk_last_error_message(), "");
    }
    const uint8_t byte = 0;
    EXPECT_EQ(mtk_shape_read(&byte, 0, &shape), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_read(&byte, -1, &shape), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_read(nullptr, 1, &shape), MTK_STATUS_INVALID_ARGUMENT);
}

TEST(Serialization, RejectsNullArguments) {
    ShapePtr box = make_box(1.0, 1.0, 1.0);
    mtk_bytes* bytes = nullptr;
    const uint8_t* data = nullptr;
    int64_t size = 0;
    BytesPtr valid(nullptr);
    {
        mtk_bytes* raw = nullptr;
        ASSERT_EQ(mtk_shape_write(box.get(), &raw), MTK_STATUS_OK);
        valid.reset(raw);
    }

    EXPECT_EQ(mtk_shape_write(nullptr, &bytes), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_write(box.get(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_bytes_get(nullptr, &data, &size), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_bytes_get(valid.get(), nullptr, &size), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_bytes_get(valid.get(), &data, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_read(data, 1, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    mtk_bytes_release(nullptr);
}

TEST(Tessellation, TurnsABoxIntoTwelveTrianglesFacingOutwards) {
    ShapePtr box = make_box(10.0, 20.0, 30.0);

    TessellationPtr tessellation = tessellate(box, 1.0);
    const mtk_mesh_info mesh = mesh_of(tessellation);
    const MeshMeasures measures = measure(mesh);

    EXPECT_EQ(mesh.material, -1);
    EXPECT_EQ(mesh.texture_coordinates, nullptr);
    EXPECT_EQ(mesh.vertex_count, 24) << "each face has its own vertices";
    EXPECT_EQ(mesh.triangle_count, 12);
    EXPECT_NEAR(measures.area, 2200.0, 1e-6);
    EXPECT_NEAR(measures.volume, 6000.0, 1e-6);
    EXPECT_EQ(measures.triangles_against_their_normals, 0);
    EXPECT_EQ(measures.vertices_with_normals_of_any_other_length, 0);
}

TEST(Tessellation, ApproachesTheAreaAndVolumeOfCurvedSolidsFromTheInside) {
    struct Case {
        const char* name;
        ShapePtr shape;
        double area;
        double volume;
    };
    std::vector<Case> cases;
    mtk_shape* shape = nullptr;
    ASSERT_EQ(mtk_shape_make_cylinder(10.0, 40.0, &shape), MTK_STATUS_OK);
    cases.push_back({"cylinder", ShapePtr(shape), 2.0 * pi * 10.0 * (10.0 + 40.0), pi * 10.0 * 10.0 * 40.0});
    ASSERT_EQ(mtk_shape_make_sphere(25.0, &shape), MTK_STATUS_OK);
    cases.push_back({"sphere", ShapePtr(shape), 4.0 * pi * 25.0 * 25.0, 4.0 / 3.0 * pi * 25.0 * 25.0 * 25.0});
    ASSERT_EQ(mtk_shape_make_cone(30.0, 10.0, 60.0, &shape), MTK_STATUS_OK);
    cases.push_back({"cone", ShapePtr(shape),
                     pi * (30.0 + 10.0) * std::hypot(60.0, 20.0) + pi * (30.0 * 30.0 + 10.0 * 10.0),
                     pi * 60.0 / 3.0 * (30.0 * 30.0 + 30.0 * 10.0 + 10.0 * 10.0)});
    ASSERT_EQ(mtk_shape_make_torus(50.0, 10.0, &shape), MTK_STATUS_OK);
    cases.push_back({"torus", ShapePtr(shape), 4.0 * pi * pi * 50.0 * 10.0, 2.0 * pi * pi * 50.0 * 10.0 * 10.0});

    for (const Case& test : cases) {
        const MeshMeasures measures = measure(mesh_of(tessellate(test.shape, 0.01, 0.2)));

        EXPECT_NEAR(measures.area, test.area, 0.005 * test.area) << test.name;
        EXPECT_NEAR(measures.volume, test.volume, 0.005 * test.volume) << test.name;
        EXPECT_LT(measures.volume, test.volume) << test.name << ": the triangles stay inside the surface";
        EXPECT_EQ(measures.triangles_against_their_normals, 0) << test.name;
        EXPECT_EQ(measures.vertices_with_normals_of_any_other_length, 0) << test.name;
    }
}

TEST(Tessellation, DependsOnlyOnTheDeflectionAsked) {
    mtk_shape* shape = nullptr;
    ASSERT_EQ(mtk_shape_make_sphere(25.0, &shape), MTK_STATUS_OK);
    ShapePtr sphere(shape);
    ASSERT_EQ(mtk_shape_make_sphere(25.0, &shape), MTK_STATUS_OK);
    ShapePtr fresh(shape);

    const int coarse = mesh_of(tessellate(fresh, 1.0)).triangle_count;
    const int fine = mesh_of(tessellate(sphere, 0.01)).triangle_count;
    const int coarse_again = mesh_of(tessellate(sphere, 1.0)).triangle_count;

    EXPECT_GT(fine, 10 * coarse);
    EXPECT_EQ(coarse_again, coarse) << "a finer mesh of the same shape is not reused";
}

TEST(Tessellation, KeepsTheShapeAsItWasAndItsPlacement) {
    ShapePtr box = make_box(10.0, 20.0, 30.0);
    ShapePtr placed = place(box, {0, -1, 0, 100, 1, 0, 0, 200, 0, 0, 1, 300});

    const MeshMeasures measures = measure(mesh_of(tessellate(placed, 1.0)));

    const std::array<double, 6> expected = {80, 200, 300, 100, 210, 330};
    for (size_t i = 0; i < 6; ++i) {
        EXPECT_NEAR(measures.bounds[i], expected[i], 1e-6) << "value " << i;
    }
    EXPECT_NEAR(measures.volume, 6000.0, 1e-6);
    EXPECT_EQ(measures.triangles_against_their_normals, 0);
    EXPECT_NEAR(volume_of(placed), 6000.0, 1e-6);
    EXPECT_EQ(mesh_of(tessellate(box, 1.0)).triangle_count, 12);
}

TEST(Tessellation, GivesTheTrianglesOfEachFace) {
    ShapePtr box = make_box(10.0, 20.0, 30.0);
    mtk_shape* made = nullptr;
    ASSERT_EQ(mtk_shape_make_cylinder(5.0, 10.0, &made), MTK_STATUS_OK);
    ShapePtr cylinder(made);

    EXPECT_EQ(faces_of(tessellate(box, 1.0)), (std::vector<int32_t>{0, 2, 4, 6, 8, 10, 12}));

    TessellationPtr tessellation = tessellate(cylinder, 0.1);
    const std::vector<int32_t> faces = faces_of(tessellation);
    ASSERT_EQ(faces.size(), 4u); // the side, the top and the bottom
    EXPECT_EQ(faces.front(), 0);
    EXPECT_EQ(faces.back(), mesh_of(tessellation).triangle_count);
    EXPECT_TRUE(std::is_sorted(faces.begin(), faces.end()));
}

TEST(Tessellation, GivesEachEdgeOnceWithoutSeamsOrPoles) {
    ShapePtr box = make_box(10.0, 20.0, 30.0);
    const std::vector<Polyline> box_edges = edges_of(tessellate(box, 1.0));
    ASSERT_EQ(box_edges.size(), 12u);
    for (const Polyline& edge : box_edges) {
        ASSERT_EQ(edge.size(), 2u); // straight
        for (const std::array<double, 3>& point : edge) {
            EXPECT_TRUE(point[0] == 0.0 || point[0] == 10.0);
            EXPECT_TRUE(point[1] == 0.0 || point[1] == 20.0);
            EXPECT_TRUE(point[2] == 0.0 || point[2] == 30.0);
        }
    }

    // The two circles of a cylinder, closed, on its radius; not the seam of its side.
    mtk_shape* made = nullptr;
    ASSERT_EQ(mtk_shape_make_cylinder(5.0, 10.0, &made), MTK_STATUS_OK);
    ShapePtr cylinder(made);
    const std::vector<Polyline> circles = edges_of(tessellate(cylinder, 0.01));
    ASSERT_EQ(circles.size(), 2u);
    for (const Polyline& circle : circles) {
        EXPECT_GT(circle.size(), 16u);
        EXPECT_EQ(circle.front(), circle.back());
        for (const std::array<double, 3>& point : circle) {
            EXPECT_NEAR(std::hypot(point[0], point[1]), 5.0, 1e-9);
            EXPECT_TRUE(point[2] == 0.0 || point[2] == 10.0);
        }
    }

    ASSERT_EQ(mtk_shape_make_sphere(5.0, &made), MTK_STATUS_OK);
    ShapePtr sphere(made);
    EXPECT_TRUE(edges_of(tessellate(sphere, 0.1)).empty());
}

TEST(Tessellation, GivesTheEdgesOfABooleanResultWherePlaced) {
    ShapePtr box = make_box(100.0, 100.0, 100.0);
    mtk_shape* made = nullptr;
    ASSERT_EQ(mtk_shape_make_cylinder(20.0, 120.0, &made), MTK_STATUS_OK);
    ShapePtr drill = place(ShapePtr(made), {1, 0, 0, 50, 0, 1, 0, 50, 0, 0, 1, -10});
    ASSERT_EQ(mtk_shape_boolean(box.get(), drill.get(), MTK_BOOLEAN_DIFFERENCE, nullptr, &made), MTK_STATUS_OK);
    ShapePtr drilled = place(ShapePtr(made), {1, 0, 0, 1000, 0, 1, 0, 0, 0, 0, 1, 0});

    const std::vector<Polyline> edges = edges_of(tessellate(drilled, 0.1));

    ASSERT_EQ(edges.size(), 14u); // those of the box and the two circles of the hole
    for (const Polyline& edge : edges) {
        for (const std::array<double, 3>& point : edge) {
            EXPECT_GE(point[0], 1000.0 - 1e-9);
            EXPECT_LE(point[0], 1100.0 + 1e-9);
        }
    }
}

TEST(Tessellation, RejectsNullArgumentsForFacesAndEdges) {
    TessellationPtr tessellation = tessellate(make_box(1.0, 1.0, 1.0), 1.0);
    const int32_t* indices = nullptr;
    const double* points = nullptr;
    int32_t count = 0;
    EXPECT_EQ(mtk_tessellation_get_faces(nullptr, &indices, &count), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_tessellation_get_faces(tessellation.get(), nullptr, &count), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_tessellation_get_faces(tessellation.get(), &indices, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_tessellation_get_edges(nullptr, &points, &indices, &count), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_tessellation_get_edges(tessellation.get(), nullptr, &indices, &count), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_tessellation_get_edges(tessellation.get(), &points, nullptr, &count), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_tessellation_get_edges(tessellation.get(), &points, &indices, nullptr), MTK_STATUS_INVALID_ARGUMENT);
}

TEST(Tessellation, ClearsTheOutputsOfACallThatFails) {
    TessellationPtr tessellation = tessellate(make_box(1.0, 1.0, 1.0), 1.0);
    const int32_t* indices = nullptr;
    const double* points = nullptr;
    int32_t count = 0;
    ASSERT_EQ(mtk_tessellation_get_edges(tessellation.get(), &points, &indices, &count), MTK_STATUS_OK);
    ASSERT_NE(count, 0);

    EXPECT_EQ(mtk_tessellation_get_edges(nullptr, &points, &indices, &count), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(points, nullptr);
    EXPECT_EQ(indices, nullptr);
    EXPECT_EQ(count, 0);

    ASSERT_EQ(mtk_tessellation_get_faces(tessellation.get(), &indices, &count), MTK_STATUS_OK);
    EXPECT_EQ(mtk_tessellation_get_faces(nullptr, &indices, &count), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(indices, nullptr);
    EXPECT_EQ(count, 0);

    double bounds[6] = {1, 1, 1, 1, 1, 1};
    EXPECT_EQ(mtk_shape_bounds(nullptr, bounds), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_TRUE(std::all_of(std::begin(bounds), std::end(bounds), [](double value) { return value == 0.0; }));
}

// Records the progress reported by an operation, and asks it to stop after some reports.
struct ProgressRecorder {
    std::vector<double> reports;
    size_t stop_after = std::numeric_limits<size_t>::max();

    mtk_progress progress() {
        return {[](double done, void* user_data) -> int32_t {
                    auto& recorder = *static_cast<ProgressRecorder*>(user_data);
                    recorder.reports.push_back(done);
                    return recorder.reports.size() >= recorder.stop_after ? 1 : 0;
                },
                this};
    }
};

TEST(Progress, GoesFromZeroToOneWithoutGoingBack) {
    mtk_shape* made = nullptr;
    ASSERT_EQ(mtk_shape_make_sphere(25.0, &made), MTK_STATUS_OK);
    ShapePtr sphere(made);
    ShapePtr box = make_box(30.0, 30.0, 30.0);

    ProgressRecorder meshing;
    const mtk_progress meshing_progress = meshing.progress();
    mtk_tessellation* tessellation = nullptr;
    ASSERT_EQ(mtk_shape_tessellate(sphere.get(), 0.01, 0.5, &meshing_progress, &tessellation), MTK_STATUS_OK)
        << mtk_last_error_message();
    TessellationPtr owned(tessellation);

    ProgressRecorder cutting;
    const mtk_progress cutting_progress = cutting.progress();
    ASSERT_EQ(mtk_shape_boolean(box.get(), sphere.get(), MTK_BOOLEAN_DIFFERENCE, &cutting_progress, &made),
              MTK_STATUS_OK)
        << mtk_last_error_message();
    ShapePtr cut(made);

    for (const ProgressRecorder* recorder : {&meshing, &cutting}) {
        ASSERT_GE(recorder->reports.size(), 2u);
        EXPECT_EQ(recorder->reports.front(), 0.0);
        EXPECT_EQ(recorder->reports.back(), 1.0);
        EXPECT_TRUE(std::is_sorted(recorder->reports.begin(), recorder->reports.end()));
    }
}

TEST(Progress, StopsAnOperationWhenTheCallerAsks) {
    mtk_shape* made = nullptr;
    ASSERT_EQ(mtk_shape_make_sphere(25.0, &made), MTK_STATUS_OK);
    ShapePtr sphere(made);
    ShapePtr box = make_box(30.0, 30.0, 30.0);

    ProgressRecorder meshing;
    meshing.stop_after = 1;
    const mtk_progress meshing_progress = meshing.progress();
    mtk_tessellation* tessellation = nullptr;
    EXPECT_EQ(mtk_shape_tessellate(sphere.get(), 0.01, 0.5, &meshing_progress, &tessellation), MTK_STATUS_CANCELLED);
    EXPECT_EQ(tessellation, nullptr);
    EXPECT_STRNE(mtk_last_error_message(), "");

    ProgressRecorder cutting;
    cutting.stop_after = 1;
    const mtk_progress cutting_progress = cutting.progress();
    made = nullptr;
    EXPECT_EQ(mtk_shape_boolean(box.get(), sphere.get(), MTK_BOOLEAN_DIFFERENCE, &cutting_progress, &made),
              MTK_STATUS_CANCELLED);
    EXPECT_EQ(made, nullptr);

    // A stopped operation leaves the shapes as they were.
    EXPECT_GT(mesh_of(tessellate(sphere, 0.01)).triangle_count, 0);
    EXPECT_NEAR(volume_of(box), 27000.0, 1e-6);
}

TEST(Concurrency, CallsOnSharedShapesFromManyThreadsAtOnceGiveTheResultsOfOneThread) {
    // A drilled box shares its untouched faces with the box, and a placed copy all its faces with the drilled box.
    ShapePtr box = make_box(100.0, 100.0, 100.0);
    mtk_shape* made = nullptr;
    ASSERT_EQ(mtk_shape_make_cylinder(20.0, 120.0, &made), MTK_STATUS_OK);
    ShapePtr drill = place(ShapePtr(made), {1, 0, 0, 50, 0, 1, 0, 50, 0, 0, 1, -10});
    ASSERT_EQ(mtk_shape_boolean(box.get(), drill.get(), MTK_BOOLEAN_DIFFERENCE, nullptr, &made), MTK_STATUS_OK);
    ShapePtr drilled(made);
    ShapePtr placed = place(drilled, {0, -1, 0, 500, 1, 0, 0, 0, 0, 0, 1, 0});
    const ShapePtr* shapes[] = {&box, &drilled, &placed};

    struct Expected {
        int coarse_triangles;
        int fine_triangles;
        double volume;
    };
    std::vector<Expected> expected;
    for (const ShapePtr* shape : shapes) {
        expected.push_back({mesh_of(tessellate(*shape, 1.0)).triangle_count,
                            mesh_of(tessellate(*shape, 0.05)).triangle_count, volume_of(*shape)});
    }
    const double drilled_volume = volume_of(drilled);

    std::atomic<int> failures{0};
    const auto work = [&](int thread) {
        for (int round = 0; round < 10; ++round) {
            const int index = (thread + round) % 3;
            const mtk_shape* shape = shapes[index]->get();
            const bool fine = (thread + round) % 2 == 0;

            mtk_tessellation* tessellation = nullptr;
            mtk_mesh_info mesh{};
            double volume = 0.0;
            std::array<double, 6> bounds{};
            mtk_bytes* bytes = nullptr;
            mtk_shape* cut = nullptr;
            if (mtk_shape_tessellate(shape, fine ? 0.05 : 1.0, 0.5, nullptr, &tessellation) != MTK_STATUS_OK ||
                mtk_tessellation_get_mesh(tessellation, &mesh) != MTK_STATUS_OK ||
                mesh.triangle_count !=
                    (fine ? expected[index].fine_triangles : expected[index].coarse_triangles) ||
                mtk_shape_volume(shape, &volume) != MTK_STATUS_OK ||
                std::abs(volume - expected[index].volume) > 1e-6 ||
                mtk_shape_bounds(shape, bounds.data()) != MTK_STATUS_OK ||
                mtk_shape_write(shape, &bytes) != MTK_STATUS_OK ||
                mtk_shape_boolean(box.get(), drill.get(), MTK_BOOLEAN_DIFFERENCE, nullptr, &cut) != MTK_STATUS_OK ||
                mtk_shape_volume(cut, &volume) != MTK_STATUS_OK || std::abs(volume - drilled_volume) > 1e-6) {
                ++failures;
            }
            mtk_tessellation_release(tessellation);
            mtk_bytes_release(bytes);
            mtk_shape_release(cut);
        }
    };

    std::vector<std::thread> threads;
    for (int thread = 0; thread < 8; ++thread) {
        threads.emplace_back(work, thread);
    }
    for (std::thread& thread : threads) {
        thread.join();
    }

    EXPECT_EQ(failures.load(), 0);
    // The shapes are as they were: the meshes made on the other threads did not stay on them.
    for (size_t index = 0; index < expected.size(); ++index) {
        EXPECT_EQ(mesh_of(tessellate(*shapes[index], 1.0)).triangle_count, expected[index].coarse_triangles);
        EXPECT_NEAR(volume_of(*shapes[index]), expected[index].volume, 1e-6);
    }
}

TEST(Tessellation, RejectsInvalidArguments) {
    ShapePtr box = make_box(1.0, 1.0, 1.0);
    mtk_tessellation* tessellation = nullptr;

    for (double deflection : {0.0, -1.0, nan, infinity}) {
        EXPECT_EQ(mtk_shape_tessellate(box.get(), deflection, 0.5, nullptr, &tessellation), MTK_STATUS_INVALID_ARGUMENT)
            << deflection;
    }
    for (double angle : {0.0, -0.5, 3.2, nan, infinity}) {
        EXPECT_EQ(mtk_shape_tessellate(box.get(), 0.1, angle, nullptr, &tessellation), MTK_STATUS_INVALID_ARGUMENT) << angle;
    }
    EXPECT_EQ(tessellation, nullptr);
    EXPECT_STRNE(mtk_last_error_message(), "");

    mtk_mesh_info mesh{};
    EXPECT_EQ(mtk_shape_tessellate(nullptr, 0.1, 0.5, nullptr, &tessellation), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_tessellate(box.get(), 0.1, 0.5, nullptr, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_tessellation_get_mesh(nullptr, &mesh), MTK_STATUS_INVALID_ARGUMENT);
    TessellationPtr valid = tessellate(box, 0.1);
    EXPECT_EQ(mtk_tessellation_get_mesh(valid.get(), nullptr), MTK_STATUS_INVALID_ARGUMENT);
    mtk_tessellation_release(nullptr);
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
