#include "mtk/occt_bridge.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <limits>
#include <memory>
#include <set>
#include <vector>

namespace {

struct ShapeDeleter {
    void operator()(mtk_shape* shape) const noexcept { mtk_shape_release(shape); }
};

using ShapePtr = std::unique_ptr<mtk_shape, ShapeDeleter>;

using Point = std::array<double, 3>;

mtk_curve line(Point a, Point b) {
    mtk_curve curve{};
    curve.kind = MTK_CURVE_LINE;
    std::copy(a.begin(), a.end(), curve.points);
    std::copy(b.begin(), b.end(), curve.points + 3);
    return curve;
}

mtk_curve circle(Point centre, double radius) {
    mtk_curve curve{};
    curve.kind = MTK_CURVE_CIRCLE;
    std::copy(centre.begin(), centre.end(), curve.points);
    curve.points[5] = 1.0;
    curve.radius = radius;
    return curve;
}

mtk_curve arc(Point a, Point through, Point b) {
    mtk_curve curve = line(a, b);
    curve.kind = MTK_CURVE_ARC;
    std::copy(through.begin(), through.end(), curve.points + 3);
    std::copy(b.begin(), b.end(), curve.points + 6);
    return curve;
}

std::vector<mtk_curve> polygon(const std::vector<Point>& points) {
    std::vector<mtk_curve> curves;
    for (size_t i = 0; i < points.size(); ++i) {
        curves.push_back(line(points[i], points[(i + 1) % points.size()]));
    }
    return curves;
}

struct Edited {
    ShapePtr shape;
    std::vector<int32_t> origins;
};

Edited add(const mtk_shape* shape, const std::vector<mtk_curve>& curves) {
    mtk_edit* edit = nullptr;
    EXPECT_EQ(mtk_shape_add_edges(shape, curves.data(), static_cast<int32_t>(curves.size()), &edit), MTK_STATUS_OK)
        << mtk_last_error_message();
    Edited result;
    if (edit == nullptr) {
        return result;
    }
    mtk_shape* made = nullptr;
    EXPECT_EQ(mtk_edit_get_shape(edit, &made), MTK_STATUS_OK);
    result.shape.reset(made);
    const int32_t* origins = nullptr;
    int32_t count = 0;
    EXPECT_EQ(mtk_edit_get_face_origins(edit, &origins, &count), MTK_STATUS_OK);
    result.origins.assign(origins, origins + count);
    mtk_edit_release(edit);
    return result;
}

struct Counts {
    int32_t solids, faces, edges, vertices;
};

Counts counts_of(const ShapePtr& shape) {
    std::array<int32_t, 4> values{};
    EXPECT_EQ(mtk_shape_count_elements(shape.get(), values.data()), MTK_STATUS_OK);
    return {values[0], values[1], values[2], values[3]};
}

bool is_valid(const ShapePtr& shape) {
    int32_t valid = 0;
    EXPECT_EQ(mtk_shape_is_valid(shape.get(), &valid), MTK_STATUS_OK);
    return valid == 1;
}

double area_of(const ShapePtr& shape) {
    double total = 0.0;
    for (int32_t face = 0; face < counts_of(shape).faces; ++face) {
        double area = 0.0;
        int32_t planar = 0;
        std::array<double, 3> origin{}, normal{};
        EXPECT_EQ(mtk_shape_face_info(shape.get(), face, &area, &planar, origin.data(), normal.data()), MTK_STATUS_OK);
        total += area;
    }
    return total;
}

ShapePtr square(double size) {
    Edited made = add(nullptr, polygon({{0, 0, 0}, {size, 0, 0}, {size, size, 0}, {0, size, 0}}));
    return std::move(made.shape);
}

ShapePtr box(double size) {
    mtk_shape* shape = nullptr;
    EXPECT_EQ(mtk_shape_make_box(size, size, size, &shape), MTK_STATUS_OK);
    return ShapePtr(shape);
}

TEST(AddEdges, ALoopOnTheGroundMakesAFaceWhenItCloses) {
    Edited open = add(nullptr, {line({0, 0, 0}, {100, 0, 0}), line({100, 0, 0}, {100, 60, 0}),
                                line({100, 60, 0}, {0, 60, 0})});
    EXPECT_EQ(counts_of(open.shape).faces, 0);
    EXPECT_EQ(counts_of(open.shape).edges, 3);

    Edited closed = add(open.shape.get(), {line({0, 60, 0}, {0, 0, 0})});
    EXPECT_EQ(counts_of(closed.shape).faces, 1);
    EXPECT_EQ(counts_of(closed.shape).edges, 4);
    EXPECT_EQ(closed.origins, std::vector<int32_t>{-1});
    EXPECT_NEAR(area_of(closed.shape), 6000.0, 1e-6);
    EXPECT_TRUE(is_valid(closed.shape));
}

TEST(AddEdges, AConcaveLoopMakesAFace) {
    Edited made = add(nullptr, polygon({{0, 0, 0}, {100, 0, 0}, {100, 10, 0}, {10, 10, 0}, {10, 100, 0}, {0, 100, 0}}));
    EXPECT_EQ(counts_of(made.shape).faces, 1);
    EXPECT_NEAR(area_of(made.shape), 1900.0, 1e-6);
}

TEST(AddEdges, AnEdgeAcrossAFaceSplitsItAndBothPartsComeFromIt) {
    ShapePtr face = square(100);
    Edited split = add(face.get(), {line({0, 50, 0}, {100, 50, 0})});
    EXPECT_EQ(counts_of(split.shape).faces, 2);
    EXPECT_EQ(split.origins, (std::vector<int32_t>{0, 0}));
    EXPECT_NEAR(area_of(split.shape), 10000.0, 1e-6);
    EXPECT_TRUE(is_valid(split.shape));
}

TEST(AddEdges, AnEdgeIntoAFaceLiesOnIt) {
    ShapePtr face = square(100);
    Edited edited = add(face.get(), {line({0, 50, 0}, {50, 50, 0})});
    EXPECT_EQ(counts_of(edited.shape).faces, 1);
    EXPECT_EQ(counts_of(edited.shape).edges, 6); // the left side split, and the new edge
    EXPECT_EQ(edited.origins, std::vector<int32_t>{0});
}

TEST(AddEdges, ALoopOnAFaceSplitsItIntoARingAndAnInnerFace) {
    ShapePtr face = square(100);
    Edited ring = add(face.get(), polygon({{30, 30, 0}, {70, 30, 0}, {70, 70, 0}, {30, 70, 0}}));
    EXPECT_EQ(counts_of(ring.shape).faces, 2);
    EXPECT_EQ(ring.origins, (std::vector<int32_t>{0, 0}));
    EXPECT_NEAR(area_of(ring.shape), 10000.0, 1e-6);

    Edited disc = add(face.get(), {circle({50, 50, 0}, 20)});
    EXPECT_EQ(counts_of(disc.shape).faces, 2);
    EXPECT_EQ(disc.origins, (std::vector<int32_t>{0, 0}));
}

TEST(AddEdges, ACircleMakesADiscAndAnArcClosedByALineAHalfDisc) {
    Edited disc = add(nullptr, {circle({0, 0, 0}, 20)});
    EXPECT_EQ(counts_of(disc.shape).faces, 1);
    EXPECT_NEAR(area_of(disc.shape), M_PI * 400, 1e-6);

    Edited half = add(nullptr, {arc({-20, 0, 0}, {0, 20, 0}, {20, 0, 0}), line({20, 0, 0}, {-20, 0, 0})});
    EXPECT_EQ(counts_of(half.shape).faces, 1);
    EXPECT_NEAR(area_of(half.shape), M_PI * 200, 1e-6);
}

TEST(AddEdges, ARectangleOnASolidSplitsItsTopAndKeepsTheSolid) {
    ShapePtr solid = box(100);
    Edited edited = add(solid.get(), polygon({{20, 20, 100}, {80, 20, 100}, {80, 80, 100}, {20, 80, 100}}));
    const Counts counts = counts_of(edited.shape);
    EXPECT_EQ(counts.solids, 1);
    EXPECT_EQ(counts.faces, 7);
    EXPECT_TRUE(is_valid(edited.shape));
    double volume = 0.0;
    EXPECT_EQ(mtk_shape_volume(edited.shape.get(), &volume), MTK_STATUS_OK);
    EXPECT_NEAR(volume, 1e6, 1e-3);

    // Every face comes from a face of the box; the top, and only the top, from two.
    std::array<int, 6> pieces{};
    for (int32_t origin : edited.origins) {
        ASSERT_GE(origin, 0);
        ++pieces[origin];
    }
    EXPECT_EQ(std::count(pieces.begin(), pieces.end(), 2), 1);
    EXPECT_EQ(std::count(pieces.begin(), pieces.end(), 1), 5);
}

TEST(AddEdges, EdgesUpFromABoxCloseALoopWithItsEdge) {
    ShapePtr solid = box(100);
    Edited edited =
        add(solid.get(), {line({0, 0, 100}, {0, 0, 150}), line({0, 0, 150}, {100, 0, 150}),
                          line({100, 0, 150}, {100, 0, 100})});
    EXPECT_EQ(counts_of(edited.shape).solids, 1);
    EXPECT_EQ(counts_of(edited.shape).faces, 7);
    EXPECT_EQ(std::count(edited.origins.begin(), edited.origins.end(), -1), 1);
    EXPECT_TRUE(is_valid(edited.shape));
}

TEST(AddEdges, PointsWithinTheToleranceAreTheSamePoint) {
    Edited joined = add(nullptr, {line({0, 0, 0}, {100, 0, 0}), line({100, 0, 0}, {100, 100, 0}),
                                  line({100, 100, 0}, {1e-8, 1e-8, 0})});
    EXPECT_EQ(counts_of(joined.shape).vertices, 3);
    EXPECT_EQ(counts_of(joined.shape).faces, 1);

    Edited apart = add(nullptr, {line({0, 0, 0}, {100, 0, 0}), line({100, 0, 0}, {100, 100, 0}),
                                 line({100, 100, 0}, {1e-4, 1e-4, 0})});
    EXPECT_EQ(counts_of(apart.shape).vertices, 4);
    EXPECT_EQ(counts_of(apart.shape).faces, 0);
}

TEST(AddEdges, ManyCrossingEdgesKeepTheShapeValid) {
    ShapePtr shape = square(1000);
    unsigned state = 7;
    const auto next = [&] { return (state = state * 1103515245u + 12345u) >> 16; };
    for (int i = 0; i < 40; ++i) {
        const Point a{50.0 * (next() % 21), 50.0 * (next() % 21), 0}, b{50.0 * (next() % 21), 50.0 * (next() % 21), 0};
        if (a == b) {
            continue;
        }
        Edited edited = add(shape.get(), {line(a, b)});
        ASSERT_TRUE(edited.shape) << "edge " << i;
        shape = std::move(edited.shape);
    }
    EXPECT_TRUE(is_valid(shape));
    EXPECT_GT(counts_of(shape).faces, 40);
    EXPECT_NEAR(area_of(shape), 1e6, 1e-3);
}

// Two vertices at the same point that are not the same vertex: a drawing that looks connected but is not.
int duplicated_vertices(const ShapePtr& shape) {
    std::vector<Point> points;
    for (int32_t vertex = 0; vertex < counts_of(shape).vertices; ++vertex) {
        Point point{};
        EXPECT_EQ(mtk_shape_vertex_point(shape.get(), vertex, point.data()), MTK_STATUS_OK);
        points.push_back(point);
    }
    int duplicates = 0;
    for (size_t i = 0; i < points.size(); ++i) {
        for (size_t j = i + 1; j < points.size(); ++j) {
            const double dx = points[i][0] - points[j][0], dy = points[i][1] - points[j][1],
                         dz = points[i][2] - points[j][2];
            duplicates += std::sqrt(dx * dx + dy * dy + dz * dz) < 1e-6 ? 1 : 0;
        }
    }
    return duplicates;
}

// Lines between the points of a grid in space close loops in many planes: the parts left out of the fuse must keep
// sharing their vertices with those that take part, which the fuse copies.
TEST(AddEdges, ADrawingInSpaceKeepsItsVerticesShared) {
    for (unsigned seed : {1u, 2u, 5u}) {
        ShapePtr shape;
        unsigned state = seed;
        const auto next = [&] { return (state = state * 1103515245u + 12345u) >> 16; };
        for (int i = 0; i < 45; ++i) {
            const Point a{500.0 * (next() % 4), 500.0 * (next() % 4), 500.0 * (next() % 3)};
            const Point b{500.0 * (next() % 4), 500.0 * (next() % 4), 500.0 * (next() % 3)};
            if (a == b) {
                continue;
            }
            const std::vector<mtk_curve> curves{line(a, b)};
            mtk_edit* edit = nullptr;
            if (mtk_shape_add_edges(shape.get(), curves.data(), 1, &edit) != MTK_STATUS_OK) {
                continue; // refused, as the editor refuses a result that is not valid
            }
            mtk_shape* made = nullptr;
            ASSERT_EQ(mtk_edit_get_shape(edit, &made), MTK_STATUS_OK);
            mtk_edit_release(edit);
            shape.reset(made);
            ASSERT_EQ(duplicated_vertices(shape), 0) << "seed " << seed << ", line " << i;
        }
        EXPECT_GT(counts_of(shape).faces, 3) << "seed " << seed;
    }
}

TEST(AddEdges, RejectsDegenerateCurvesAndNullArguments) {
    mtk_edit* edit = reinterpret_cast<mtk_edit*>(1);
    const mtk_curve point = line({1, 1, 1}, {1, 1, 1});
    EXPECT_EQ(mtk_shape_add_edges(nullptr, &point, 1, &edit), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(edit, nullptr);
    const mtk_curve straight = arc({0, 0, 0}, {1, 0, 0}, {2, 0, 0});
    EXPECT_EQ(mtk_shape_add_edges(nullptr, &straight, 1, &edit), MTK_STATUS_INVALID_ARGUMENT);
    const mtk_curve flat = circle({0, 0, 0}, 0);
    EXPECT_EQ(mtk_shape_add_edges(nullptr, &flat, 1, &edit), MTK_STATUS_INVALID_ARGUMENT);
    mtk_curve infinite = line({0, 0, 0}, {1, 0, 0});
    infinite.points[4] = std::numeric_limits<double>::infinity();
    EXPECT_EQ(mtk_shape_add_edges(nullptr, &infinite, 1, &edit), MTK_STATUS_INVALID_ARGUMENT);
    mtk_curve other = line({0, 0, 0}, {1, 0, 0});
    other.kind = MTK_CURVE_OTHER;
    EXPECT_EQ(mtk_shape_add_edges(nullptr, &other, 1, &edit), MTK_STATUS_INVALID_ARGUMENT);
    const mtk_curve good = line({0, 0, 0}, {1, 0, 0});
    EXPECT_EQ(mtk_shape_add_edges(nullptr, &good, 0, &edit), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_add_edges(nullptr, nullptr, 1, &edit), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_add_edges(nullptr, &good, 1, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_edit_get_shape(nullptr, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    mtk_edit_release(nullptr);
}

TEST(EdgeInfo, DescribesLinesArcsAndCircles) {
    Edited lines = add(nullptr, {line({0, 0, 0}, {30, 40, 0})});
    mtk_edge_info info{};
    ASSERT_EQ(mtk_shape_edge_info(lines.shape.get(), 0, &info), MTK_STATUS_OK) << mtk_last_error_message();
    EXPECT_EQ(info.kind, MTK_CURVE_LINE);
    EXPECT_EQ(info.closed, 0);
    EXPECT_NEAR(info.length, 50.0, 1e-9);
    EXPECT_NEAR(info.middle[0], 15.0, 1e-9);
    EXPECT_NEAR(info.middle[1], 20.0, 1e-9);
    EXPECT_NEAR(info.start_tangent[0], 0.6, 1e-9);
    EXPECT_NEAR(info.end_tangent[1], 0.8, 1e-9);
    EXPECT_EQ(info.radius, 0.0);

    Edited half = add(nullptr, {arc({20, 0, 0}, {0, 20, 0}, {-20, 0, 0})});
    ASSERT_EQ(mtk_shape_edge_info(half.shape.get(), 0, &info), MTK_STATUS_OK);
    EXPECT_EQ(info.kind, MTK_CURVE_ARC);
    EXPECT_NEAR(info.length, M_PI * 20, 1e-9);
    EXPECT_NEAR(info.middle[1], 20.0, 1e-9);
    EXPECT_NEAR(info.radius, 20.0, 1e-9);
    EXPECT_NEAR(std::abs(info.normal[2]), 1.0, 1e-9);
    EXPECT_NEAR(info.start[0], 20.0, 1e-9);
    EXPECT_NEAR(info.start_tangent[1], 1.0, 1e-9); // leaving (20, 0) towards (0, 20)

    Edited disc = add(nullptr, {circle({5, 5, 0}, 10)});
    ASSERT_EQ(mtk_shape_edge_info(disc.shape.get(), 0, &info), MTK_STATUS_OK);
    EXPECT_EQ(info.kind, MTK_CURVE_CIRCLE);
    EXPECT_EQ(info.closed, 1);
    EXPECT_NEAR(info.centre[0], 5.0, 1e-9);

    EXPECT_EQ(mtk_shape_edge_info(disc.shape.get(), 1, &info), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_edge_info(nullptr, 0, &info), MTK_STATUS_INVALID_ARGUMENT);
}


Edited push_pull(const ShapePtr& shape, int32_t face, double distance) {
    mtk_edit* edit = nullptr;
    EXPECT_EQ(mtk_shape_push_pull(shape.get(), face, distance, &edit), MTK_STATUS_OK) << mtk_last_error_message();
    Edited result;
    if (edit == nullptr) {
        return result;
    }
    mtk_shape* made = nullptr;
    EXPECT_EQ(mtk_edit_get_shape(edit, &made), MTK_STATUS_OK);
    result.shape.reset(made);
    const int32_t* origins = nullptr;
    int32_t count = 0;
    EXPECT_EQ(mtk_edit_get_face_origins(edit, &origins, &count), MTK_STATUS_OK);
    result.origins.assign(origins, origins + count);
    mtk_edit_release(edit);
    return result;
}

struct Face {
    double area;
    Point normal;
    double offset; // of the plane along the normal
};

Face face_of(const ShapePtr& shape, int32_t face) {
    Face result{};
    int32_t planar = 0;
    Point origin{};
    EXPECT_EQ(mtk_shape_face_info(shape.get(), face, &result.area, &planar, origin.data(), result.normal.data()),
              MTK_STATUS_OK);
    result.offset = origin[0] * result.normal[0] + origin[1] * result.normal[1] + origin[2] * result.normal[2];
    return result;
}

// The faces of a shape on a plane, by its normal and its offset along it.
std::vector<int32_t> faces_on(const ShapePtr& shape, Point normal, double offset) {
    std::vector<int32_t> found;
    for (int32_t face = 0; face < counts_of(shape).faces; ++face) {
        const Face info = face_of(shape, face);
        if (std::abs(info.normal[0] - normal[0]) < 1e-9 && std::abs(info.normal[1] - normal[1]) < 1e-9 &&
            std::abs(info.normal[2] - normal[2]) < 1e-9 && std::abs(info.offset - offset) < 1e-6) {
            found.push_back(face);
        }
    }
    return found;
}

double volume_of(const ShapePtr& shape) {
    double volume = 0.0;
    EXPECT_EQ(mtk_shape_volume(shape.get(), &volume), MTK_STATUS_OK);
    return volume;
}

TEST(PushPull, PullingTheTopOfABoxMakesItTallerWithTheSameFaces) {
    ShapePtr solid = box(100);
    const std::vector<int32_t> top = faces_on(solid, {0, 0, 1}, 100);
    ASSERT_EQ(top.size(), 1u);

    Edited taller = push_pull(solid, top[0], 50);
    const Counts counts = counts_of(taller.shape);
    EXPECT_EQ(counts.solids, 1);
    EXPECT_EQ(counts.faces, 6);
    EXPECT_EQ(counts.edges, 12);
    EXPECT_NEAR(volume_of(taller.shape), 1.5e6, 1e-3);
    EXPECT_TRUE(is_valid(taller.shape));
    EXPECT_EQ(std::set<int32_t>(taller.origins.begin(), taller.origins.end()), (std::set<int32_t>{0, 1, 2, 3, 4, 5}));
    const std::vector<int32_t> new_top = faces_on(taller.shape, {0, 0, 1}, 150);
    ASSERT_EQ(new_top.size(), 1u);
    EXPECT_EQ(taller.origins[new_top[0]], top[0]);
}

TEST(PushPull, PushingTheTopIntoABoxLowersIt) {
    ShapePtr solid = box(100);
    Edited lower = push_pull(solid, faces_on(solid, {0, 0, 1}, 100)[0], -30);
    EXPECT_EQ(counts_of(lower.shape).solids, 1);
    EXPECT_EQ(counts_of(lower.shape).faces, 6);
    EXPECT_NEAR(volume_of(lower.shape), 0.7e6, 1e-3);
    EXPECT_TRUE(is_valid(lower.shape));
}

TEST(PushPull, PullingAPartOfTheTopMakesAStepWhoseFacesComeFromIt) {
    ShapePtr solid = box(100);
    Edited drawn = add(solid.get(), polygon({{20, 20, 100}, {80, 20, 100}, {80, 80, 100}, {20, 80, 100}}));
    int32_t inner = -1;
    for (int32_t face : faces_on(drawn.shape, {0, 0, 1}, 100)) {
        if (std::abs(face_of(drawn.shape, face).area - 3600) < 1e-6) {
            inner = face;
        }
    }
    ASSERT_GE(inner, 0);

    Edited step = push_pull(drawn.shape, inner, 40);
    EXPECT_EQ(counts_of(step.shape).solids, 1);
    EXPECT_EQ(counts_of(step.shape).faces, 11);
    EXPECT_NEAR(volume_of(step.shape), 1e6 + 3600 * 40, 1e-3);
    EXPECT_TRUE(is_valid(step.shape));
    EXPECT_EQ(std::count(step.origins.begin(), step.origins.end(), inner), 5); // the four sides and the top

    Edited pocket = push_pull(drawn.shape, inner, -40);
    EXPECT_EQ(counts_of(pocket.shape).faces, 11);
    EXPECT_NEAR(volume_of(pocket.shape), 1e6 - 3600 * 40, 1e-3);
    EXPECT_TRUE(is_valid(pocket.shape));
    EXPECT_EQ(std::count(pocket.origins.begin(), pocket.origins.end(), inner), 5); // the four walls and the bottom
}

TEST(PushPull, EdgesDrawnOnTheSidesStay) {
    ShapePtr solid = box(100);
    Edited drawn = add(solid.get(), {line({50, 0, 0}, {50, 0, 100})});
    ASSERT_EQ(faces_on(drawn.shape, {0, -1, 0}, 0).size(), 2u);

    Edited taller = push_pull(drawn.shape, faces_on(drawn.shape, {0, 0, 1}, 100)[0], 50);
    EXPECT_NEAR(volume_of(taller.shape), 1.5e6, 1e-3);
    EXPECT_TRUE(is_valid(taller.shape));
    EXPECT_GE(faces_on(taller.shape, {0, -1, 0}, 0).size(), 2u);
    EXPECT_EQ(faces_on(taller.shape, {1, 0, 0}, 100).size(), 1u);
}

TEST(PushPull, AFaceAloneBecomesASolidEitherWay) {
    ShapePtr face = square(100);
    for (const double distance : {30.0, -30.0}) {
        Edited made = push_pull(face, 0, distance);
        const Counts counts = counts_of(made.shape);
        EXPECT_EQ(counts.solids, 1);
        EXPECT_EQ(counts.faces, 6);
        EXPECT_NEAR(volume_of(made.shape), 3e5, 1e-3);
        EXPECT_TRUE(is_valid(made.shape));
        EXPECT_EQ(made.origins, std::vector<int32_t>(6, 0));
    }
}

TEST(PushPull, AFaceNextToAnotherStaysConnectedToIt) {
    ShapePtr face = square(100);
    Edited split = add(face.get(), {line({50, 0, 0}, {50, 100, 0})});
    ASSERT_EQ(counts_of(split.shape).faces, 2);

    Edited made = push_pull(split.shape, 0, 20);
    const Counts counts = counts_of(made.shape);
    EXPECT_EQ(counts.solids, 1);
    EXPECT_EQ(counts.faces, 7); // the solid and the other face
    EXPECT_EQ(counts.vertices, 10);
    EXPECT_TRUE(is_valid(made.shape));
}

TEST(PushPull, RejectsWhatCannotBePushedOrPulled) {
    ShapePtr solid = box(100);
    const int32_t top = faces_on(solid, {0, 0, 1}, 100)[0];
    mtk_edit* edit = nullptr;
    EXPECT_EQ(mtk_shape_push_pull(solid.get(), top, -100, &edit), MTK_STATUS_KERNEL_ERROR);
    EXPECT_EQ(mtk_shape_push_pull(solid.get(), top, 0, &edit), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_push_pull(solid.get(), top, std::numeric_limits<double>::quiet_NaN(), &edit),
              MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_push_pull(solid.get(), 6, 10, &edit), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_push_pull(nullptr, 0, 10, &edit), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(edit, nullptr);

    mtk_shape* cylinder = nullptr;
    ASSERT_EQ(mtk_shape_make_cylinder(10, 40, &cylinder), MTK_STATUS_OK);
    ShapePtr round(cylinder);
    int32_t curved = -1;
    for (int32_t face = 0; face < counts_of(round).faces; ++face) {
        double area = 0.0;
        int32_t planar = 1;
        Point origin{}, normal{};
        mtk_shape_face_info(round.get(), face, &area, &planar, origin.data(), normal.data());
        if (planar == 0) {
            curved = face;
        }
    }
    ASSERT_GE(curved, 0);
    EXPECT_EQ(mtk_shape_push_pull(round.get(), curved, 10, &edit), MTK_STATUS_INVALID_ARGUMENT);
}

TEST(FaceEdges, GivesTheEdgesThatBoundAFace) {
    ShapePtr face = square(100);
    int32_t count = 0;
    EXPECT_EQ(mtk_shape_face_edges(face.get(), 0, nullptr, 0, &count), MTK_STATUS_OK);
    EXPECT_EQ(count, 4);
    std::array<int32_t, 4> edges{};
    EXPECT_EQ(mtk_shape_face_edges(face.get(), 0, edges.data(), 4, &count), MTK_STATUS_OK);
    EXPECT_EQ(std::set<int32_t>(edges.begin(), edges.end()), (std::set<int32_t>{0, 1, 2, 3}));

    ShapePtr ring = std::move(add(face.get(), {circle({50, 50, 0}, 20)}).shape);
    for (const int32_t expected : {5, 1}) { // the square and the circle, or the circle alone
        bool seen = false;
        for (int32_t index = 0; index < 2; ++index) {
            EXPECT_EQ(mtk_shape_face_edges(ring.get(), index, nullptr, 0, &count), MTK_STATUS_OK);
            seen = seen || count == expected;
        }
        EXPECT_TRUE(seen) << expected;
    }
    EXPECT_EQ(mtk_shape_face_edges(face.get(), 1, nullptr, 0, &count), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_shape_face_edges(face.get(), 0, nullptr, 2, &count), MTK_STATUS_INVALID_ARGUMENT);
}

} // namespace
