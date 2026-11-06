#include "internal.hpp"
#include "progress.hpp"

#include <BRepAdaptor_Curve.hxx>
#include <BRepBuilderAPI_Copy.hxx>
#include <BRepLib_ToolTriangulatedShape.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepTools.hxx>
#include <BRep_Tool.hxx>
#include <GCPnts_TangentialDeflection.hxx>
#include <NCollection_IndexedDataMap.hxx>
#include <NCollection_List.hxx>
#include <Poly_PolygonOnTriangulation.hxx>
#include <Poly_Triangulation.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <TopTools_ShapeMapHasher.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <gp_Dir.hxx>
#include <gp_Pnt.hxx>
#include <gp_Trsf.hxx>

#include <cmath>
#include <cstdint>
#include <limits>
#include <memory>
#include <utility>
#include <vector>

// The triangles of all the faces of a shape, as one mesh, and the polylines of its edges.
struct mtk_tessellation {
    std::vector<double> vertices;
    std::vector<float> normals;
    std::vector<int32_t> triangles;
    std::vector<int32_t> first_triangles; // of each face, then the number of triangles
    std::vector<double> edge_points;
    std::vector<int32_t> first_points; // of each edge, then the number of points
};

namespace {

constexpr double pi = 3.14159265358979323846;

// Appends the triangulation of a face, in the coordinates of the shape, with the front of its triangles and its normals
// on the outside of the solid.
void append_face(const TopoDS_Face& face, mtk_tessellation& mesh) {
    TopLoc_Location location;
    const occ::handle<Poly_Triangulation> triangulation = BRep_Tool::Triangulation(face, location);
    if (triangulation.IsNull() || triangulation->NbTriangles() == 0) {
        return;
    }
    BRepLib_ToolTriangulatedShape::ComputeNormals(face, triangulation);

    const gp_Trsf transform = location.Transformation();
    const bool reversed = face.Orientation() == TopAbs_REVERSED;
    const bool mirrored = transform.VectorialPart().Determinant() < 0.0;
    const auto first_vertex = static_cast<int32_t>(mesh.vertices.size() / 3);

    for (int node = 1; node <= triangulation->NbNodes(); ++node) {
        const gp_Pnt point = triangulation->Node(node).Transformed(transform);
        mesh.vertices.insert(mesh.vertices.end(), {point.X(), point.Y(), point.Z()});

        gp_Dir normal = triangulation->Normal(node).Transformed(transform);
        if (reversed) {
            normal.Reverse();
        }
        mesh.normals.insert(mesh.normals.end(), {static_cast<float>(normal.X()), static_cast<float>(normal.Y()),
                                                 static_cast<float>(normal.Z())});
    }

    // Both a reversed face and a mirroring transformation turn the front of the triangles to the other side.
    const bool flip = reversed != mirrored;
    for (int element = 1; element <= triangulation->NbTriangles(); ++element) {
        int a = 0;
        int b = 0;
        int c = 0;
        triangulation->Triangle(element).Get(a, b, c);
        if (flip) {
            std::swap(b, c);
        }
        mesh.triangles.insert(mesh.triangles.end(),
                              {first_vertex + a - 1, first_vertex + b - 1, first_vertex + c - 1});
    }
}

// Whether an edge is drawn: not a seam, where a closed surface meets itself, nor a degenerate edge, such as a pole.
bool is_drawn(const TopoDS_Edge& edge, const NCollection_List<TopoDS_Shape>& faces) {
    if (BRep_Tool::Degenerated(edge)) {
        return false;
    }
    for (const TopoDS_Shape& face : faces) {
        if (BRep_Tool::IsClosed(edge, TopoDS::Face(face))) {
            return false;
        }
    }
    return true;
}

// Appends the polyline of an edge, in the coordinates of the shape: the vertices of the mesh along it where it bounds a
// meshed face, so that it lies on the triangles, otherwise points of its curve within the deflection.
void append_edge(const TopoDS_Edge& edge, const NCollection_List<TopoDS_Shape>& faces, double linear_deflection,
                 double angular_deflection, mtk_tessellation& mesh) {
    std::vector<gp_Pnt> points;
    for (const TopoDS_Shape& face : faces) {
        TopLoc_Location location;
        const occ::handle<Poly_Triangulation> triangulation = BRep_Tool::Triangulation(TopoDS::Face(face), location);
        if (triangulation.IsNull()) {
            continue;
        }
        const occ::handle<Poly_PolygonOnTriangulation> polygon =
            BRep_Tool::PolygonOnTriangulation(edge, triangulation, location);
        if (polygon.IsNull()) {
            continue;
        }
        const gp_Trsf transform = location.Transformation();
        for (int node = 1; node <= polygon->NbNodes(); ++node) {
            points.push_back(triangulation->Node(polygon->Node(node)).Transformed(transform));
        }
        break;
    }
    if (points.empty()) {
        const BRepAdaptor_Curve curve(edge);
        const GCPnts_TangentialDeflection sampled(curve, angular_deflection, linear_deflection);
        for (int point = 1; point <= sampled.NbPoints(); ++point) {
            points.push_back(sampled.Value(point));
        }
    }
    if (points.size() < 2) {
        return;
    }

    mesh.first_points.push_back(static_cast<int32_t>(mesh.edge_points.size() / 3));
    for (const gp_Pnt& point : points) {
        mesh.edge_points.insert(mesh.edge_points.end(), {point.X(), point.Y(), point.Z()});
    }
}

// Too many vertices or points to count with 32 bits, three values each.
bool too_many(size_t values) {
    return values / 3 > static_cast<size_t>(std::numeric_limits<int32_t>::max() / 3);
}

} // namespace

mtk_status mtk_shape_tessellate(const mtk_shape* shape, double linear_deflection, double angular_deflection,
                                const mtk_progress* progress, mtk_tessellation** out_tessellation) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_tessellation);
        if (shape == nullptr || out_tessellation == nullptr) {
            return mtk::invalid_argument("shape and out_tessellation must not be null.");
        }
        if (!std::isfinite(linear_deflection) || linear_deflection <= 0.0) {
            return mtk::invalid_argument("The linear deflection must be a positive finite number.");
        }
        if (!std::isfinite(angular_deflection) || angular_deflection <= 0.0 || angular_deflection > pi) {
            return mtk::invalid_argument("The angular deflection must be greater than 0 and not greater than pi.");
        }

        // Meshing attaches triangulations to the faces it meshes. It meshes a copy of the topology, which shares the
        // geometry, so that the shape and those sharing its faces are not written to, and calls on them can run on
        // other threads at the same time; the triangulations go with the copy. The copy starts without any, even if
        // the shape was read with some, so that the mesh depends only on the deflection. The faces are meshed in
        // parallel.
        const TopoDS_Shape copy = BRepBuilderAPI_Copy(shape->value, false, false).Shape();
        BRepTools::Clean(copy);
        IMeshTools_Parameters parameters;
        parameters.Deflection = linear_deflection;
        parameters.Angle = angular_deflection;
        parameters.InParallel = true;
        const occ::handle<mtk::Progress> indicator = new mtk::Progress(progress);
        BRepMesh_IncrementalMesh mesher(copy, parameters, indicator->start());
        indicator->finish();

        auto result = std::make_unique<mtk_tessellation>();
        for (TopExp_Explorer faces(copy, TopAbs_FACE); faces.More(); faces.Next()) {
            result->first_triangles.push_back(static_cast<int32_t>(result->triangles.size() / 3));
            append_face(TopoDS::Face(faces.Current()), *result);
            if (too_many(result->vertices.size())) {
                return mtk::invalid_argument("The shape has too many triangles for this deflection.");
            }
        }
        if (result->triangles.empty()) {
            return mtk::invalid_argument("The shape has no faces to turn into triangles.");
        }
        result->first_triangles.push_back(static_cast<int32_t>(result->triangles.size() / 3));

        // Each edge once, with the faces it bounds.
        NCollection_IndexedDataMap<TopoDS_Shape, NCollection_List<TopoDS_Shape>, TopTools_ShapeMapHasher> edges;
        TopExp::MapShapesAndAncestors(copy, TopAbs_EDGE, TopAbs_FACE, edges);
        for (int index = 1; index <= edges.Extent(); ++index) {
            const TopoDS_Edge& edge = TopoDS::Edge(edges.FindKey(index));
            if (is_drawn(edge, edges.FindFromIndex(index))) {
                append_edge(edge, edges.FindFromIndex(index), linear_deflection, angular_deflection, *result);
                if (too_many(result->edge_points.size())) {
                    return mtk::invalid_argument("The shape has too many edge points for this deflection.");
                }
            }
        }
        result->first_points.push_back(static_cast<int32_t>(result->edge_points.size() / 3));

        *out_tessellation = result.release();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_tessellation_get_mesh(const mtk_tessellation* tessellation, mtk_mesh_info* out_mesh) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_mesh);
        if (tessellation == nullptr || out_mesh == nullptr) {
            return mtk::invalid_argument("tessellation and out_mesh must not be null.");
        }
        out_mesh->material = -1;
        out_mesh->vertex_count = static_cast<int32_t>(tessellation->vertices.size() / 3);
        out_mesh->triangle_count = static_cast<int32_t>(tessellation->triangles.size() / 3);
        out_mesh->vertices = tessellation->vertices.data();
        out_mesh->normals = tessellation->normals.data();
        out_mesh->triangles = tessellation->triangles.data();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_tessellation_get_faces(const mtk_tessellation* tessellation, const int32_t** out_first_triangles,
                                      int32_t* out_face_count) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_first_triangles, out_face_count);
        if (tessellation == nullptr || out_first_triangles == nullptr || out_face_count == nullptr) {
            return mtk::invalid_argument("tessellation, out_first_triangles and out_face_count must not be null.");
        }
        *out_first_triangles = tessellation->first_triangles.data();
        *out_face_count = static_cast<int32_t>(tessellation->first_triangles.size() - 1);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_tessellation_get_edges(const mtk_tessellation* tessellation, const double** out_points,
                                      const int32_t** out_first_points, int32_t* out_edge_count) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_points, out_first_points, out_edge_count);
        if (tessellation == nullptr || out_points == nullptr || out_first_points == nullptr ||
            out_edge_count == nullptr) {
            return mtk::invalid_argument("tessellation, out_points, out_first_points and out_edge_count must not be "
                                         "null.");
        }
        *out_points = tessellation->edge_points.data();
        *out_first_points = tessellation->first_points.data();
        *out_edge_count = static_cast<int32_t>(tessellation->first_points.size() - 1);
        return MTK_STATUS_OK;
    });
}

void mtk_tessellation_release(mtk_tessellation* tessellation) {
    delete tessellation;
}
