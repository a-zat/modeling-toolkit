// Drawing edges on shapes: General Fuse of the curves with the parts of the shape near them, then faces for the planar
// loops they close. The design and its measures are in issue #64.

#include "internal.hpp"

#include <BOPTools_AlgoTools3D.hxx>
#include <BRepAdaptor_Curve.hxx>
#include <BRepAlgoAPI_BuilderAlgo.hxx>
#include <BRepAlgoAPI_Splitter.hxx>
#include <BRepBndLib.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepClass_FaceClassifier.hxx>
#include <BRepGProp.hxx>
#include <BRepTools_History.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <Bnd_Box.hxx>
#include <ElSLib.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <GProp_GProps.hxx>
#include <IntTools_Context.hxx>
#include <Precision.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_ShapeMapHasher.hxx>
#include <NCollection_IndexedDataMap.hxx>
#include <NCollection_IndexedMap.hxx>
#include <NCollection_List.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <gp_Circ.hxx>
#include <gp_Pln.hxx>

#include <algorithm>
#include <array>
#include <cmath>
#include <optional>
#include <vector>

// The managed mirrors in Interop/EditStructs.cs have these sizes.
static_assert(sizeof(mtk_curve) == 88);
static_assert(sizeof(mtk_edge_info) == 192);

struct mtk_edit {
    TopoDS_Shape shape;
    std::vector<int32_t> face_origins;
};

namespace {

using ShapeList = NCollection_List<TopoDS_Shape>;
using ShapeMap = NCollection_IndexedMap<TopoDS_Shape, TopTools_ShapeMapHasher>;
// The shapes of a kind, each with the shapes of another kind that hold it.
using AncestorMap = NCollection_IndexedDataMap<TopoDS_Shape, ShapeList, TopTools_ShapeMapHasher>;

constexpr double plane_tolerance = 1e-6;

struct Step {
    TopoDS_Shape shape;
    occ::handle<BRepTools_History> history;
};

gp_Pnt point_at(const double* values) {
    return gp_Pnt(values[0], values[1], values[2]);
}

bool finite(const mtk_curve& curve) {
    return std::all_of(std::begin(curve.points), std::end(curve.points), [](double v) { return std::isfinite(v); }) &&
           std::isfinite(curve.radius);
}

// The edge of a curve; nothing for a degenerate one.
std::optional<TopoDS_Edge> edge_of(const mtk_curve& curve) {
    if (!finite(curve)) {
        return std::nullopt;
    }
    const gp_Pnt a = point_at(curve.points), b = point_at(curve.points + 3), c = point_at(curve.points + 6);
    switch (curve.kind) {
    case MTK_CURVE_LINE:
        if (a.Distance(b) <= Precision::Confusion()) {
            return std::nullopt;
        }
        return BRepBuilderAPI_MakeEdge(a, b).Edge();
    case MTK_CURVE_ARC: {
        if (a.Distance(b) <= Precision::Confusion() || b.Distance(c) <= Precision::Confusion() ||
            gp_Vec(a, b).Crossed(gp_Vec(a, c)).Magnitude() <= Precision::Confusion() * a.Distance(c)) {
            return std::nullopt;
        }
        GC_MakeArcOfCircle arc(a, b, c);
        if (!arc.IsDone()) {
            return std::nullopt;
        }
        return BRepBuilderAPI_MakeEdge(arc.Value()).Edge();
    }
    case MTK_CURVE_CIRCLE: {
        const gp_Vec normal(b.XYZ());
        if (curve.radius <= Precision::Confusion() || normal.Magnitude() <= Precision::Confusion()) {
            return std::nullopt;
        }
        return BRepBuilderAPI_MakeEdge(gp_Circ(gp_Ax2(a, gp_Dir(normal)), curve.radius)).Edge();
    }
    default:
        return std::nullopt;
    }
}

// The parts of a shape that a fuse keeps whole: its solids, the shells and faces outside solids, the edges outside
// faces and the vertices outside edges.
std::vector<TopoDS_Shape> items_of(const TopoDS_Shape& shape) {
    std::vector<TopoDS_Shape> items;
    if (shape.IsNull()) {
        return items;
    }
    ShapeMap taken;
    for (const TopAbs_ShapeEnum kind : {TopAbs_SOLID, TopAbs_SHELL, TopAbs_FACE, TopAbs_EDGE, TopAbs_VERTEX}) {
        for (TopExp_Explorer part(shape, kind); part.More(); part.Next()) {
            if (!taken.Contains(part.Current())) {
                items.push_back(part.Current());
                TopExp::MapShapes(part.Current(), taken);
            }
        }
    }
    return items;
}

// General Fuse of shapes added to a shape, with only the items of the shape whose box meets them and those connected to
// them through shared vertices; the others stay as they are. The fuse leaves its arguments unchanged, copying the
// vertices and edges it changes, so an item that shares one with an argument must be an argument too, or the copy and
// the original would both remain, at the same place but no longer shared. Only the items that took part are checked.
Step fuse_into(const TopoDS_Shape& shape, const std::vector<TopoDS_Shape>& added) {
    Bnd_Box reach;
    for (const TopoDS_Shape& part : added) {
        BRepBndLib::Add(part, reach);
    }
    reach.Enlarge(Precision::Confusion());

    const std::vector<TopoDS_Shape> items = items_of(shape);
    std::vector<bool> taking_part(items.size(), false);
    ShapeMap reached_vertices;
    for (size_t i = 0; i < items.size(); ++i) {
        Bnd_Box box;
        BRepBndLib::Add(items[i], box);
        taking_part[i] = !box.IsOut(reach);
        if (taking_part[i]) {
            TopExp::MapShapes(items[i], TopAbs_VERTEX, reached_vertices);
        }
    }
    for (bool grew = true; grew;) {
        grew = false;
        for (size_t i = 0; i < items.size(); ++i) {
            if (taking_part[i]) {
                continue;
            }
            for (TopExp_Explorer vertex(items[i], TopAbs_VERTEX); vertex.More(); vertex.Next()) {
                if (reached_vertices.Contains(vertex.Current())) {
                    taking_part[i] = true;
                    TopExp::MapShapes(items[i], TopAbs_VERTEX, reached_vertices);
                    grew = true;
                    break;
                }
            }
        }
    }

    ShapeList arguments;
    std::vector<TopoDS_Shape> kept;
    for (size_t i = 0; i < items.size(); ++i) {
        if (taking_part[i]) {
            arguments.Append(items[i]);
        } else {
            kept.push_back(items[i]);
        }
    }
    for (const TopoDS_Shape& part : added) {
        arguments.Append(part);
    }

    TopoDS_Shape fused;
    occ::handle<BRepTools_History> history;
    if (arguments.Extent() == 1) {
        // General Fuse needs two shapes at least; a curve alone is taken as it is.
        fused = arguments.First();
        history = new BRepTools_History();
    } else {
        BRepAlgoAPI_BuilderAlgo builder;
        builder.SetArguments(arguments);
        builder.SetRunParallel(false);
        // The shapes are left as they are, tolerances included, as other threads may be reading them.
        builder.SetNonDestructive(true);
        builder.Build();
        if (!builder.IsDone() || builder.HasErrors()) {
            throw mtk::kernel_error("The curves could not be added to the shape.");
        }
        fused = builder.Shape();
        history = builder.History();
    }
    if (!BRepCheck_Analyzer(fused).IsValid()) {
        throw mtk::kernel_error("Adding the curves would make a shape that is not valid.");
    }

    BRep_Builder builder;
    TopoDS_Compound compound;
    builder.MakeCompound(compound);
    for (const TopoDS_Shape& item : kept) {
        builder.Add(compound, item);
    }
    for (const TopoDS_Shape& item : items_of(fused)) {
        builder.Add(compound, item);
    }
    return {compound, history};
}

bool is_line(const TopoDS_Edge& edge) {
    return BRepAdaptor_Curve(edge).GetType() == GeomAbs_Line;
}

gp_Dir direction_of(const TopoDS_Edge& edge) {
    TopoDS_Vertex first, last;
    TopExp::Vertices(edge, first, last);
    return gp_Dir(gp_Vec(BRep_Tool::Pnt(first), BRep_Tool::Pnt(last)));
}

// The plane of a circle or an arc; nothing for other curves.
std::optional<gp_Pln> plane_of_circle(const TopoDS_Edge& edge) {
    const BRepAdaptor_Curve curve(edge);
    if (curve.GetType() != GeomAbs_Circle) {
        return std::nullopt;
    }
    return gp_Pln(curve.Circle().Position());
}

bool in_plane(const TopoDS_Edge& edge, const gp_Pln& plane) {
    const BRepAdaptor_Curve curve(edge);
    for (int i = 0; i <= 4; ++i) {
        const double parameter = curve.FirstParameter() + (curve.LastParameter() - curve.FirstParameter()) * i / 4;
        if (plane.Distance(curve.Value(parameter)) > plane_tolerance) {
            return false;
        }
    }
    return true;
}

bool same_plane(const gp_Pln& a, const gp_Pln& b) {
    return a.Axis().Direction().IsParallel(b.Axis().Direction(), 1e-9) &&
           a.Distance(b.Location()) < plane_tolerance;
}

bool has_face(const AncestorMap& faces_of, const TopoDS_Shape& edge) {
    return faces_of.Contains(edge) && !faces_of.FindFromKey(edge).IsEmpty();
}

// The faces bounded by the loops that new edges, not on a face, close with the edges of the shape in a plane. The
// planes are those of each new line and each line at its ends; a closed edge, such as a circle, bounds a face alone.
std::vector<TopoDS_Shape> loop_faces(const TopoDS_Shape& shape, const std::vector<TopoDS_Edge>& new_edges) {
    AncestorMap edges_of, faces_of;
    TopExp::MapShapesAndAncestors(shape, TopAbs_VERTEX, TopAbs_EDGE, edges_of);
    TopExp::MapShapesAndAncestors(shape, TopAbs_EDGE, TopAbs_FACE, faces_of);

    std::vector<TopoDS_Shape> made;
    std::vector<gp_Pln> planes;
    std::vector<TopoDS_Edge> loose;
    for (const TopoDS_Edge& edge : new_edges) {
        if (has_face(faces_of, edge)) {
            continue; // on a face, which the fuse has split already
        }
        loose.push_back(edge);
        if (BRep_Tool::IsClosed(edge)) {
            BRepBuilderAPI_MakeFace face(BRepBuilderAPI_MakeWire(edge).Wire(), true);
            if (face.IsDone()) {
                made.push_back(face.Face());
            }
            continue;
        }
        const auto consider = [&](const gp_Pln& plane) {
            if (in_plane(edge, plane) &&
                std::none_of(planes.begin(), planes.end(), [&](const gp_Pln& p) { return same_plane(p, plane); })) {
                planes.push_back(plane);
            }
        };
        if (const std::optional<gp_Pln> own = plane_of_circle(edge)) {
            consider(*own);
            continue;
        }
        if (!is_line(edge)) {
            continue;
        }
        const gp_Dir along = direction_of(edge);
        TopoDS_Vertex ends[2];
        TopExp::Vertices(edge, ends[0], ends[1]);
        for (const TopoDS_Vertex& end : ends) {
            for (const TopoDS_Shape& next : edges_of.FindFromKey(end)) {
                const TopoDS_Edge& neighbour = TopoDS::Edge(next);
                if (neighbour.IsSame(edge)) {
                    continue;
                }
                if (const std::optional<gp_Pln> arc = plane_of_circle(neighbour)) {
                    consider(*arc);
                } else if (is_line(neighbour)) {
                    const gp_Dir other = direction_of(neighbour);
                    if (!along.IsParallel(other, 1e-9)) {
                        consider(gp_Pln(BRep_Tool::Pnt(end), along.Crossed(other)));
                    }
                }
            }
        }
    }

    ShapeMap faces;
    TopExp::MapShapes(shape, TopAbs_FACE, faces);
    for (const gp_Pln& plane : planes) {
        // The edges in the plane connected to the new ones split a face of the plane larger than them all.
        ShapeList tools;
        Bnd_Box box;
        ShapeMap reached;
        std::vector<TopoDS_Shape> pending(loose.begin(), loose.end());
        while (!pending.empty()) {
            const TopoDS_Edge edge = TopoDS::Edge(pending.back());
            pending.pop_back();
            if (reached.Contains(edge) || !in_plane(edge, plane)) {
                continue;
            }
            reached.Add(edge);
            tools.Append(edge);
            BRepBndLib::Add(edge, box);
            for (TopExp_Explorer vertex(edge, TopAbs_VERTEX); vertex.More(); vertex.Next()) {
                for (const TopoDS_Shape& next : edges_of.FindFromKey(vertex.Current())) {
                    pending.push_back(next);
                }
            }
        }
        if (tools.Extent() < 2) { // two at least: a line and an arc, say
            continue;
        }
        double xmin, ymin, zmin, xmax, ymax, zmax;
        box.Get(xmin, ymin, zmin, xmax, ymax, zmax);
        const double size = 2.0 * gp_Pnt(xmin, ymin, zmin).Distance(gp_Pnt(xmax, ymax, zmax)) + 10.0;
        double u, v;
        ElSLib::Parameters(plane, gp_Pnt((xmin + xmax) / 2, (ymin + ymax) / 2, (zmin + zmax) / 2), u, v);
        const TopoDS_Face large = BRepBuilderAPI_MakeFace(plane, u - size, u + size, v - size, v + size);

        BRepAlgoAPI_Splitter splitter;
        splitter.SetArguments(ShapeList{large});
        splitter.SetTools(tools);
        splitter.SetRunParallel(false);
        splitter.SetNonDestructive(true);
        splitter.Build();
        if (!splitter.IsDone() || splitter.HasErrors()) {
            continue;
        }

        // The border of the large face and its pieces: a piece that touches it is not bounded by drawn edges.
        ShapeMap border;
        for (TopExp_Explorer edge(large, TopAbs_EDGE); edge.More(); edge.Next()) {
            border.Add(edge.Current());
            for (const TopoDS_Shape& piece : splitter.Modified(edge.Current())) {
                border.Add(piece);
            }
        }
        const occ::handle<IntTools_Context> context = new IntTools_Context();
        for (TopExp_Explorer piece(splitter.Shape(), TopAbs_FACE); piece.More(); piece.Next()) {
            const TopoDS_Face& face = TopoDS::Face(piece.Current());
            bool bounded = true;
            for (TopExp_Explorer edge(face, TopAbs_EDGE); edge.More() && bounded; edge.Next()) {
                bounded = !border.Contains(edge.Current());
            }
            if (!bounded) {
                continue;
            }
            // Not a face of the shape already: a point inside it is on none of them.
            gp_Pnt inside;
            gp_Pnt2d inside_uv;
            if (BOPTools_AlgoTools3D::PointInFace(face, inside, inside_uv, context) != 0) {
                continue;
            }
            bool existing = false;
            for (int i = 1; i <= faces.Extent() && !existing; ++i) {
                Bnd_Box face_box;
                BRepBndLib::Add(faces(i), face_box);
                existing = !face_box.IsOut(inside) &&
                           BRepClass_FaceClassifier(TopoDS::Face(faces(i)), inside, plane_tolerance).State() ==
                               TopAbs_IN;
            }
            if (!existing) {
                made.push_back(face);
            }
        }
    }
    return made;
}

// For each face of the new shape, the face of the old one it comes from, by index from 0, or -1.
std::vector<int32_t> face_origins(const TopoDS_Shape& before, const Step& after) {
    ShapeMap old_faces, new_faces;
    if (!before.IsNull()) {
        TopExp::MapShapes(before, TopAbs_FACE, old_faces);
    }
    TopExp::MapShapes(after.shape, TopAbs_FACE, new_faces);
    std::vector<int32_t> origins(new_faces.Extent(), -1);
    const auto take = [&](const TopoDS_Shape& face, int32_t origin) {
        const int index = new_faces.FindIndex(face);
        if (index > 0 && origins[index - 1] < 0) {
            origins[index - 1] = origin;
        }
    };
    for (int i = 1; i <= old_faces.Extent(); ++i) {
        const TopoDS_Shape& face = old_faces(i);
        if (after.history->IsRemoved(face)) {
            continue;
        }
        const ShapeList& pieces = after.history->Modified(face);
        if (pieces.IsEmpty()) {
            take(face, i - 1);
        }
        for (const TopoDS_Shape& piece : pieces) {
            take(piece, i - 1);
        }
    }
    return origins;
}

// Adds one curve: the fuse, then the faces of the loops it closes.
Step add_edge(const TopoDS_Shape& shape, const TopoDS_Edge& edge) {
    Step fused = fuse_into(shape, {edge});

    std::vector<TopoDS_Edge> pieces;
    const ShapeList& modified = fused.history->Modified(edge);
    if (modified.IsEmpty()) {
        pieces.push_back(edge);
    }
    for (const TopoDS_Shape& piece : modified) {
        pieces.push_back(TopoDS::Edge(piece));
    }

    const std::vector<TopoDS_Shape> faces = loop_faces(fused.shape, pieces);
    if (faces.empty()) {
        return fused;
    }
    Step closed = fuse_into(fused.shape, faces);
    occ::handle<BRepTools_History> history = new BRepTools_History(*fused.history);
    history->Merge(closed.history);
    return {closed.shape, history};
}

void copy_point(const gp_Pnt& point, double* out) {
    out[0] = point.X();
    out[1] = point.Y();
    out[2] = point.Z();
}

void copy_vector(const gp_Vec& vector, double* out) {
    out[0] = vector.X();
    out[1] = vector.Y();
    out[2] = vector.Z();
}

} // namespace

mtk_status mtk_shape_add_edges(const mtk_shape* shape, const mtk_curve* curves, int32_t curve_count,
                               mtk_edit** out_edit) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_edit);
        if (curves == nullptr || out_edit == nullptr) {
            return mtk::invalid_argument("curves and out_edit must not be null.");
        }
        if (curve_count < 1) {
            return mtk::invalid_argument("curve_count must be at least 1.");
        }
        std::vector<TopoDS_Edge> edges;
        for (int32_t i = 0; i < curve_count; ++i) {
            const std::optional<TopoDS_Edge> edge = edge_of(curves[i]);
            if (!edge) {
                return mtk::invalid_argument(
                    "A curve is not a line, an arc or a circle, is not finite, or is degenerate.");
            }
            edges.push_back(*edge);
        }

        TopoDS_Shape current = shape == nullptr ? TopoDS_Shape() : shape->value;
        std::vector<int32_t> origins;
        {
            NCollection_IndexedMap<TopoDS_Shape, TopTools_ShapeMapHasher> faces;
            if (!current.IsNull()) {
                TopExp::MapShapes(current, TopAbs_FACE, faces);
            }
            for (int i = 0; i < faces.Extent(); ++i) {
                origins.push_back(i);
            }
        }
        for (const TopoDS_Edge& edge : edges) {
            const Step step = add_edge(current, edge);
            std::vector<int32_t> next = face_origins(current, step);
            for (int32_t& origin : next) {
                origin = origin < 0 ? -1 : origins[origin];
            }
            origins = std::move(next);
            current = step.shape;
        }

        *out_edit = new mtk_edit{current, std::move(origins)};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_edit_get_shape(const mtk_edit* edit, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_shape);
        if (edit == nullptr || out_shape == nullptr) {
            return mtk::invalid_argument("edit and out_shape must not be null.");
        }
        *out_shape = new mtk_shape{edit->shape};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_edit_get_face_origins(const mtk_edit* edit, const int32_t** out_origins, int32_t* out_count) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_origins, out_count);
        if (edit == nullptr || out_origins == nullptr || out_count == nullptr) {
            return mtk::invalid_argument("edit, out_origins and out_count must not be null.");
        }
        *out_origins = edit->face_origins.data();
        *out_count = static_cast<int32_t>(edit->face_origins.size());
        return MTK_STATUS_OK;
    });
}

void mtk_edit_release(mtk_edit* edit) {
    delete edit;
}

mtk_status mtk_shape_edge_info(const mtk_shape* shape, int32_t edge, mtk_edge_info* out_info) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_info);
        if (shape == nullptr || out_info == nullptr) {
            return mtk::invalid_argument("shape and out_info must not be null.");
        }
        ShapeMap edges;
        TopExp::MapShapes(shape->value, TopAbs_EDGE, edges);
        if (edge < 0 || edge >= edges.Extent()) {
            return mtk::invalid_argument("edge is not an edge of the shape.");
        }

        const TopoDS_Edge& value = TopoDS::Edge(edges(edge + 1));
        const BRepAdaptor_Curve curve(value);
        const bool reversed = value.Orientation() == TopAbs_REVERSED;
        double first = curve.FirstParameter(), last = curve.LastParameter();
        mtk_edge_info info{};
        info.length = GCPnts_AbscissaPoint::Length(curve);
        info.closed = BRep_Tool::IsClosed(value) ? 1 : 0;

        gp_Pnt start, end;
        gp_Vec start_tangent, end_tangent;
        curve.D1(first, start, start_tangent);
        curve.D1(last, end, end_tangent);
        if (reversed) {
            std::swap(start, end);
            std::swap(start_tangent, end_tangent);
            start_tangent.Reverse();
            end_tangent.Reverse();
        }
        copy_point(start, info.start);
        copy_point(end, info.end);
        if (start_tangent.Magnitude() > gp::Resolution()) {
            copy_vector(start_tangent.Normalized(), info.start_tangent);
        }
        if (end_tangent.Magnitude() > gp::Resolution()) {
            copy_vector(end_tangent.Normalized(), info.end_tangent);
        }
        const GCPnts_AbscissaPoint half(curve, info.length / 2, first);
        copy_point(curve.Value(half.IsDone() ? half.Parameter() : (first + last) / 2), info.middle);

        switch (curve.GetType()) {
        case GeomAbs_Line:
            info.kind = MTK_CURVE_LINE;
            break;
        case GeomAbs_Circle: {
            const gp_Circ circle = curve.Circle();
            info.kind = info.closed ? MTK_CURVE_CIRCLE : MTK_CURVE_ARC;
            copy_point(circle.Location(), info.centre);
            gp_Vec normal(circle.Axis().Direction());
            if (reversed) {
                normal.Reverse();
            }
            copy_vector(normal, info.normal);
            info.radius = circle.Radius();
            break;
        }
        default:
            info.kind = MTK_CURVE_OTHER;
            break;
        }
        *out_info = info;
        return MTK_STATUS_OK;
    });
}
