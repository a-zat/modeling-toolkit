#include "internal.hpp"

#include <BRepBuilderAPI_Copy.hxx>
#include <BRepLib_ToolTriangulatedShape.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepTools.hxx>
#include <BRep_Tool.hxx>
#include <Poly_Triangulation.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <TopoDS.hxx>
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

// The triangles of all the faces of a shape, as one mesh.
struct mtk_tessellation {
    std::vector<double> vertices;
    std::vector<float> normals;
    std::vector<int32_t> triangles;
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

} // namespace

mtk_status mtk_shape_tessellate(const mtk_shape* shape, double linear_deflection, double angular_deflection,
                                mtk_tessellation** out_tessellation) {
    return mtk::guarded([&] {
        if (shape == nullptr || out_tessellation == nullptr) {
            return mtk::invalid_argument("shape and out_tessellation must not be null.");
        }
        *out_tessellation = nullptr;
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
        BRepMesh_IncrementalMesh mesher(copy, linear_deflection, false, angular_deflection, true);

        auto result = std::make_unique<mtk_tessellation>();
        for (TopExp_Explorer faces(copy, TopAbs_FACE); faces.More(); faces.Next()) {
            append_face(TopoDS::Face(faces.Current()), *result);
            if (result->vertices.size() / 3 > static_cast<size_t>(std::numeric_limits<int32_t>::max() / 3)) {
                return mtk::invalid_argument("The shape has too many triangles for this deflection.");
            }
        }
        if (result->triangles.empty()) {
            return mtk::invalid_argument("The shape has no faces to turn into triangles.");
        }

        *out_tessellation = result.release();
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_tessellation_get_mesh(const mtk_tessellation* tessellation, mtk_mesh_info* out_mesh) {
    return mtk::guarded([&] {
        if (tessellation == nullptr || out_mesh == nullptr) {
            return mtk::invalid_argument("tessellation and out_mesh must not be null.");
        }
        *out_mesh = {};
        out_mesh->material = -1;
        out_mesh->vertex_count = static_cast<int32_t>(tessellation->vertices.size() / 3);
        out_mesh->triangle_count = static_cast<int32_t>(tessellation->triangles.size() / 3);
        out_mesh->vertices = tessellation->vertices.data();
        out_mesh->normals = tessellation->normals.data();
        out_mesh->triangles = tessellation->triangles.data();
        return MTK_STATUS_OK;
    });
}

void mtk_tessellation_release(mtk_tessellation* tessellation) {
    delete tessellation;
}
