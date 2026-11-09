#include "scene.hpp"

#include "mtk/errors.hpp"

#include <Graphic3d_ArrayOfPoints.hxx>
#include <Graphic3d_AspectMarker3d.hxx>
#include <Graphic3d_Group.hxx>
#include <Graphic3d_MaterialAspect.hxx>
#include <Graphic3d_PBRMaterial.hxx>
#include <Graphic3d_TextureSet.hxx>
#include <Image_PixMap.hxx>
#include <Prs3d_Presentation.hxx>
#include <PrsMgr_PresentationManager.hxx>
#include <Select3D_SensitivePoint.hxx>
#include <Select3D_SensitivePoly.hxx>
#include <Select3D_SensitivePrimitiveArray.hxx>
#include <SelectMgr_EntityOwner.hxx>
#include <SelectMgr_Selection.hxx>
#include <gp_Mat.hxx>

#include <algorithm>
#include <cmath>
#include <cstring>

namespace {

Graphic3d_ArrayFlags flags_of(bool has_normals, bool has_uvs) {
    Graphic3d_ArrayFlags flags = Graphic3d_ArrayFlags_None;
    if (has_normals) {
        flags |= Graphic3d_ArrayFlags_VertexNormal;
    }
    if (has_uvs) {
        flags |= Graphic3d_ArrayFlags_VertexTexel;
    }
    return flags;
}

gp_Mat linear_part(const double* matrix) {
    return gp_Mat(matrix[0], matrix[1], matrix[2], matrix[4], matrix[5], matrix[6], matrix[8], matrix[9], matrix[10]);
}

// Around a selection, the other meshes keep the colours and textures of their materials, with this part of their
// opacity.
constexpr float faded_opacity = 0.25f;

occ::handle<Graphic3d_AspectFillArea3d> make_aspect(const mtk_viewer_material& material,
                                                    const occ::handle<Graphic3d_Texture2D>& texture) {
    const Quantity_Color color(material.base_color[0], material.base_color[1], material.base_color[2],
                               Quantity_TOC_RGB);
    const float opacity = material.base_color[3];

    // The view shades as OpenCASCADE does the metallic-roughness materials of glTF without physically based rendering
    // (XCAFDoc_VisMaterial): the metallic factor becomes the specular colour, the roughness the inverse of the
    // shininess. The material keeps them too, for a physically based shading.
    Graphic3d_MaterialAspect aspect_material(Graphic3d_NameOfMaterial_UserDefined);
    aspect_material.SetColor(color);
    aspect_material.SetSpecularColor(
        Quantity_Color(material.metallic, material.metallic, material.metallic, Quantity_TOC_RGB));
    aspect_material.SetShininess(1.0f - material.roughness);
    aspect_material.SetTransparency(1.0f - opacity);
    Graphic3d_PBRMaterial physical;
    physical.SetColor(Quantity_ColorRGBA(color, opacity));
    physical.SetMetallic(material.metallic);
    physical.SetRoughness(material.roughness);
    aspect_material.SetPBRMaterial(physical);

    occ::handle<Graphic3d_AspectFillArea3d> aspect = new Graphic3d_AspectFillArea3d();
    aspect->SetInteriorStyle(Aspect_IS_SOLID);
    aspect->SetFrontMaterial(aspect_material);
    aspect->SetBackMaterial(aspect_material);
    if (opacity < 1.0f) {
        aspect->SetAlphaMode(Graphic3d_AlphaMode_Blend);
    }
    if (!texture.IsNull()) {
        aspect->SetTextureSet(new Graphic3d_TextureSet(texture));
        aspect->SetTextureMapOn(true);
    }
    return aspect;
}

void apply_display_mode_to(Graphic3d_AspectFillArea3d& aspect, mtk_viewer_display_mode mode,
                           const Quantity_ColorRGBA& edges, bool triangle_edges) {
    const bool wireframe = mode == MTK_VIEWER_DISPLAY_WIREFRAME;
    aspect.SetInteriorStyle(wireframe ? Aspect_IS_EMPTY : Aspect_IS_SOLID);
    // Without faces the edges take the colour of the material, lit and textured: in a single colour they stand out
    // from the ground as in the other modes.
    aspect.SetTextureMapOn(!wireframe && !aspect.TextureSet().IsNull());
    aspect.SetShadingModel(wireframe ? Graphic3d_TypeOfShadingModel_Unlit : Graphic3d_TypeOfShadingModel_DEFAULT);
    aspect.SetInteriorColor(edges);
    aspect.SetDrawEdges(triangle_edges && mode != MTK_VIEWER_DISPLAY_SHADED);
    aspect.SetEdgeColor(edges);
    aspect.SetEdgeWidth(1.0);
}

} // namespace

void mtk::apply_display_mode(MaterialAspects& aspects, mtk_viewer_display_mode mode, const Quantity_Color& edges) {
    const Quantity_ColorRGBA shaded(edges, 1.0f), faded(edges, faded_opacity);
    apply_display_mode_to(*aspects.shaded, mode, shaded, true);
    apply_display_mode_to(*aspects.faded, mode, faded, true);
    apply_display_mode_to(*aspects.shaded_faces, mode, shaded, false);
    apply_display_mode_to(*aspects.faded_faces, mode, faded, false);
}

mtk::EdgeAspects mtk::make_edge_aspects() {
    EdgeAspects aspects{new Graphic3d_AspectLine3d(), new Graphic3d_AspectLine3d()};
    aspects.faded->SetAlphaMode(Graphic3d_AlphaMode_Blend);
    return aspects;
}

void mtk::apply_display_mode(EdgeAspects& aspects, mtk_viewer_display_mode mode, const Quantity_Color& edges) {
    // An empty line type draws nothing: the same presentations serve all the display modes.
    const Aspect_TypeOfLine type = mode == MTK_VIEWER_DISPLAY_SHADED ? Aspect_TOL_EMPTY : Aspect_TOL_SOLID;
    for (const occ::handle<Graphic3d_AspectLine3d>& aspect : {aspects.shaded, aspects.faded}) {
        aspect->SetLineType(type);
        aspect->SetLineWidth(1.0f);
    }
    aspects.shaded->SetInteriorColor(Quantity_ColorRGBA(edges, 1.0f));
    aspects.faded->SetInteriorColor(Quantity_ColorRGBA(edges, faded_opacity));
}

mtk::MeshObject::MeshObject(const Mesh& mesh, const MaterialAspects& aspects, const EdgeAspects& edge_aspects)
    : mesh_(mesh), aspects_(aspects), edge_aspects_(edge_aspects) {}

void mtk::MeshObject::Compute(const occ::handle<PrsMgr_PresentationManager>&,
                              const occ::handle<Prs3d_Presentation>& presentation, const int mode) {
    if (!AcceptDisplayMode(mode)) {
        return;
    }
    const bool faded = mode == faded_mode;
    const occ::handle<Graphic3d_Group> faces = presentation->NewGroup();
    if (mesh_.has_own_edges) {
        faces->SetGroupPrimitivesAspect(faded ? aspects_.faded_faces : aspects_.shaded_faces);
    } else {
        faces->SetGroupPrimitivesAspect(faded ? aspects_.faded : aspects_.shaded);
    }
    faces->AddPrimitiveArray(mesh_.triangles);

    if (!mesh_.edges.IsNull()) {
        const occ::handle<Graphic3d_Group> edges = presentation->NewGroup();
        edges->SetGroupPrimitivesAspect(faded ? edge_aspects_.faded : edge_aspects_.shaded);
        edges->AddPrimitiveArray(mesh_.edges);
    }
}

void mtk::MeshObject::ComputeSelection(const occ::handle<SelectMgr_Selection>& selection, const int mode) {
    if (mode != 0) {
        return;
    }
    if (mesh_.elements) {
        add_element_sensitives(selection, this, mesh_);
        return;
    }
    const occ::handle<Select3D_SensitivePrimitiveArray> sensitive =
        new Select3D_SensitivePrimitiveArray(new SelectMgr_EntityOwner(this));
    sensitive->InitTriangulation(mesh_.triangles->Attributes(), mesh_.triangles->Indices(), TopLoc_Location());
    selection->Add(sensitive);
}

namespace {

// Selected faces are tinted more than hovered ones; edges and vertices are drawn opaque.
constexpr float hovered_face_opacity = 0.35f;
constexpr float selected_face_opacity = 0.6f;
constexpr float highlighted_edge_width = 2.0f; // drawn 1 pixel wide where OpenGL has only thin lines, as on macOS
constexpr double highlighted_vertex_scale = 3.0;

// Element owners are picked with priority to vertices, then edges, then faces, at about the same depth.
int priority_of(mtk_viewer_element_kind kind) {
    return kind == MTK_VIEWER_ELEMENT_VERTEX ? 7 : kind == MTK_VIEWER_ELEMENT_EDGE ? 6 : 5;
}

// The points of a polyline of an array, from 0.
std::vector<gp_Pnt> polyline_points(const Graphic3d_ArrayOfPolylines& polylines, int polyline) {
    int first = 1;
    for (int bound = 1; bound <= polyline; ++bound) {
        first += polylines.Bound(bound);
    }
    std::vector<gp_Pnt> points;
    for (int vertex = first; vertex < first + polylines.Bound(polyline + 1); ++vertex) {
        points.push_back(polylines.Vertice(vertex));
    }
    return points;
}

} // namespace

mtk::ElementOwner::ElementOwner(const occ::handle<SelectMgr_SelectableObject>& object, const Mesh& mesh,
                                mtk_viewer_element_kind kind, int index, int polyline)
    : SelectMgr_EntityOwner(object, priority_of(kind)), mesh_(mesh), kind_(kind), index_(index), polyline_(polyline) {}

void mtk::ElementOwner::draw(const occ::handle<Prs3d_Presentation>& presentation, const Quantity_Color& color,
                             float opacity) const {
    presentation->Clear();
    // Over the scene, hidden by what is in front of the element; with the placement of the object that shows it.
    presentation->SetZLayer(Graphic3d_ZLayerId_Top);
    presentation->SetTransformation(Selectable()->TransformationGeom());
    const occ::handle<Graphic3d_Group> group = presentation->NewGroup();
    if (kind_ == MTK_VIEWER_ELEMENT_FACE) {
        occ::handle<Graphic3d_AspectFillArea3d> aspect = new Graphic3d_AspectFillArea3d();
        aspect->SetInteriorStyle(Aspect_IS_SOLID);
        aspect->SetShadingModel(Graphic3d_TypeOfShadingModel_Unlit);
        aspect->SetInteriorColor(Quantity_ColorRGBA(color, opacity));
        aspect->SetAlphaMode(Graphic3d_AlphaMode_Blend);
        aspect->SetPolygonOffsets(Aspect_POM_Fill, -1.0f, -1.0f); // over the face itself
        group->SetGroupPrimitivesAspect(aspect);
        const Graphic3d_ArrayOfTriangles& triangles = *mesh_.triangles;
        const bool indexed = triangles.EdgeNumber() > 0;
        const int first = mesh_.elements->first_triangles[index_], end = mesh_.elements->first_triangles[index_ + 1];
        occ::handle<Graphic3d_ArrayOfTriangles> face = new Graphic3d_ArrayOfTriangles(3 * (end - first));
        for (int corner = 3 * first + 1; corner <= 3 * end; ++corner) {
            face->AddVertex(triangles.Vertice(indexed ? triangles.Edge(corner) : corner));
        }
        group->AddPrimitiveArray(face);
    } else if (kind_ == MTK_VIEWER_ELEMENT_EDGE) {
        group->SetGroupPrimitivesAspect(new Graphic3d_AspectLine3d(color, Aspect_TOL_SOLID, highlighted_edge_width));
        const std::vector<gp_Pnt> points = polyline_points(*mesh_.edges, polyline_);
        occ::handle<Graphic3d_ArrayOfPolylines> line = new Graphic3d_ArrayOfPolylines(static_cast<int>(points.size()));
        for (const gp_Pnt& point : points) {
            line->AddVertex(point);
        }
        group->AddPrimitiveArray(line);
    } else {
        group->SetGroupPrimitivesAspect(
            new Graphic3d_AspectMarker3d(Aspect_TOM_BALL, color, highlighted_vertex_scale));
        occ::handle<Graphic3d_ArrayOfPoints> point = new Graphic3d_ArrayOfPoints(1);
        point->AddVertex(mesh_.elements->vertices[index_]);
        group->AddPrimitiveArray(point);
    }
}

void mtk::ElementOwner::HilightWithColor(const occ::handle<PrsMgr_PresentationManager>& manager,
                                         const occ::handle<Prs3d_Drawer>& style, const int) {
    if (hover_.IsNull()) {
        hover_ = new Prs3d_Presentation(manager->StructureManager());
    }
    draw(hover_, style->Color(), hovered_face_opacity);
    if (manager->IsImmediateModeOn()) {
        manager->AddToImmediateList(hover_);
    } else {
        hover_->Display();
    }
}

void mtk::ElementOwner::Unhilight(const occ::handle<PrsMgr_PresentationManager>&, const int) {
    if (!hover_.IsNull()) {
        hover_->Erase();
    }
}

void mtk::ElementOwner::highlight_selected(const occ::handle<PrsMgr_PresentationManager>& manager,
                                           const occ::handle<Prs3d_Drawer>& style) {
    if (selected_.IsNull()) {
        selected_ = new Prs3d_Presentation(manager->StructureManager());
    }
    draw(selected_, style->Color(), selected_face_opacity);
    selected_->Display();
}

void mtk::ElementOwner::unhighlight_selected() {
    if (!selected_.IsNull()) {
        selected_->Erase();
    }
}

void mtk::add_element_sensitives(const occ::handle<SelectMgr_Selection>& selection,
                                 const occ::handle<SelectMgr_SelectableObject>& object, const Mesh& mesh) {
    const Elements& elements = *mesh.elements;
    for (size_t face = 0; face + 1 < elements.first_triangles.size(); ++face) {
        const int first = elements.first_triangles[face], end = elements.first_triangles[face + 1];
        if (first == end) {
            continue;
        }
        occ::handle<Select3D_SensitivePrimitiveArray> sensitive = new Select3D_SensitivePrimitiveArray(
            new ElementOwner(object, mesh, MTK_VIEWER_ELEMENT_FACE, static_cast<int>(face)));
        // The range of the indices includes its last one.
        sensitive->InitTriangulation(mesh.triangles->Attributes(), mesh.triangles->Indices(), TopLoc_Location(),
                                     3 * first, 3 * end - 1);
        selection->Add(sensitive);
    }
    for (size_t polyline = 0; polyline < elements.edge_indices.size(); ++polyline) {
        const std::vector<gp_Pnt> points = polyline_points(*mesh.edges, static_cast<int>(polyline));
        NCollection_Array1<gp_Pnt> array(1, static_cast<int>(points.size()));
        for (size_t point = 0; point < points.size(); ++point) {
            array.SetValue(static_cast<int>(point) + 1, points[point]);
        }
        selection->Add(new Select3D_SensitivePoly(
            new ElementOwner(object, mesh, MTK_VIEWER_ELEMENT_EDGE, elements.edge_indices[polyline],
                             static_cast<int>(polyline)),
            array, true));
    }
    for (size_t vertex = 0; vertex < elements.vertices.size(); ++vertex) {
        selection->Add(new Select3D_SensitivePoint(
            new ElementOwner(object, mesh, MTK_VIEWER_ELEMENT_VERTEX, static_cast<int>(vertex)),
            elements.vertices[vertex]));
    }
}

void mtk::ConnectedMesh::ComputeSelection(const occ::handle<SelectMgr_Selection>& selection, const int mode) {
    const occ::handle<MeshObject> mesh = occ::down_cast<MeshObject>(ConnectedTo());
    if (mode != 0 || mesh.IsNull() || !mesh->mesh().elements) {
        AIS_ConnectedInteractive::ComputeSelection(selection, mode);
        return;
    }
    // In the coordinates of the mesh: the selector applies the placement of this object.
    add_element_sensitives(selection, this, mesh->mesh());
}

namespace {

occ::handle<Graphic3d_ArrayOfTriangles> make_triangles(const mtk_viewer_mesh& mesh) {
    const bool has_uvs = mesh.uvs != nullptr;
    const auto position = [&](int vertex) {
        return gp_Pnt(mesh.positions[3 * vertex], mesh.positions[3 * vertex + 1], mesh.positions[3 * vertex + 2]);
    };

    if (mesh.normals != nullptr) {
        occ::handle<Graphic3d_ArrayOfTriangles> triangles =
            new Graphic3d_ArrayOfTriangles(mesh.vertex_count, 3 * mesh.triangle_count, flags_of(true, has_uvs));
        for (int vertex = 0; vertex < mesh.vertex_count; ++vertex) {
            const int added = triangles->AddVertex(position(vertex));
            const float* normal = mesh.normals + 3 * vertex;
            triangles->SetVertexNormal(added, normal[0], normal[1], normal[2]);
            if (has_uvs) {
                triangles->SetVertexTexel(added, mesh.uvs[2 * vertex], mesh.uvs[2 * vertex + 1]);
            }
        }
        for (int triangle = 0; triangle < mesh.triangle_count; ++triangle) {
            const int32_t* corners = mesh.triangles + 3 * triangle;
            triangles->AddEdges(corners[0] + 1, corners[1] + 1, corners[2] + 1); // vertices count from 1
        }
        return triangles;
    }

    // Flat shading: three vertices of its own for each triangle, with the normal of the triangle.
    occ::handle<Graphic3d_ArrayOfTriangles> triangles =
        new Graphic3d_ArrayOfTriangles(3 * mesh.triangle_count, 0, flags_of(true, has_uvs));
    for (int triangle = 0; triangle < mesh.triangle_count; ++triangle) {
        const int32_t* corners = mesh.triangles + 3 * triangle;
        const gp_XYZ a = position(corners[0]).XYZ(), b = position(corners[1]).XYZ(), c = position(corners[2]).XYZ();
        gp_XYZ normal = (b - a).Crossed(c - a);
        const double length = normal.Modulus();
        normal = length > 0.0 ? normal / length : gp_XYZ(0.0, 0.0, 1.0);
        for (int corner = 0; corner < 3; ++corner) {
            const int added = triangles->AddVertex(position(corners[corner]));
            triangles->SetVertexNormal(added, normal.X(), normal.Y(), normal.Z());
            if (has_uvs) {
                triangles->SetVertexTexel(added, mesh.uvs[2 * corners[corner]], mesh.uvs[2 * corners[corner] + 1]);
            }
        }
    }
    return triangles;
}

// The polylines of the edges of its own of a checked mesh, null when it has none.
occ::handle<Graphic3d_ArrayOfPolylines> make_edges(const mtk_viewer_mesh& mesh) {
    if (mesh.edge_first_points == nullptr || mesh.edge_count == 0) {
        return nullptr;
    }
    const int32_t point_count = mesh.edge_first_points[mesh.edge_count];
    occ::handle<Graphic3d_ArrayOfPolylines> edges = new Graphic3d_ArrayOfPolylines(point_count, mesh.edge_count);
    for (int32_t edge = 0; edge < mesh.edge_count; ++edge) {
        const int32_t first = mesh.edge_first_points[edge], next = mesh.edge_first_points[edge + 1];
        edges->AddBound(next - first);
        for (int32_t point = first; point < next; ++point) {
            const double* xyz = mesh.edge_points + 3 * point;
            edges->AddVertex(gp_Pnt(xyz[0], xyz[1], xyz[2]));
        }
    }
    return edges;
}

occ::handle<Graphic3d_ArrayOfPolylines> transform_edges(const occ::handle<Graphic3d_ArrayOfPolylines>& edges,
                                                        const double* matrix) {
    if (edges.IsNull()) {
        return nullptr;
    }
    const gp_Mat linear = linear_part(matrix);
    const gp_XYZ translation(matrix[3], matrix[7], matrix[11]);
    occ::handle<Graphic3d_ArrayOfPolylines> result =
        new Graphic3d_ArrayOfPolylines(edges->VertexNumber(), edges->BoundNumber());
    int vertex = 1;
    for (int bound = 1; bound <= edges->BoundNumber(); ++bound) {
        result->AddBound(edges->Bound(bound));
        for (int point = 0; point < edges->Bound(bound); ++point, ++vertex) {
            double x = 0.0, y = 0.0, z = 0.0;
            edges->Vertice(vertex, x, y, z);
            result->AddVertex(gp_Pnt(gp_XYZ(x, y, z).Multiplied(linear) + translation));
        }
    }
    return result;
}

occ::handle<Graphic3d_ArrayOfTriangles> transform_triangles(const occ::handle<Graphic3d_ArrayOfTriangles>& triangles,
                                                            const double* matrix) {
    const gp_Mat linear = linear_part(matrix);
    const gp_XYZ translation(matrix[3], matrix[7], matrix[11]);
    // Normals follow the inverse of the transpose; a reflection turns the triangles over, so their corners are
    // swapped to keep them counter-clockwise from their front.
    const gp_Mat normal_matrix = linear.Inverted().Transposed();
    const bool reflects = linear.Determinant() < 0.0;
    const bool has_normals = triangles->HasVertexNormals(), has_uvs = triangles->HasVertexTexels();
    const bool indexed = triangles->EdgeNumber() > 0;

    occ::handle<Graphic3d_ArrayOfTriangles> result = new Graphic3d_ArrayOfTriangles(
        triangles->VertexNumber(), indexed ? triangles->EdgeNumber() : 0, flags_of(has_normals, has_uvs));
    const auto copy_vertex = [&](int vertex) {
        double x = 0.0, y = 0.0, z = 0.0;
        triangles->Vertice(vertex, x, y, z);
        const int added = result->AddVertex(gp_Pnt(gp_XYZ(x, y, z).Multiplied(linear) + translation));
        if (has_normals) {
            double nx = 0.0, ny = 0.0, nz = 0.0;
            triangles->VertexNormal(vertex, nx, ny, nz);
            gp_XYZ normal = gp_XYZ(nx, ny, nz).Multiplied(normal_matrix);
            const double length = normal.Modulus();
            normal = length > 0.0 ? normal / length : normal;
            result->SetVertexNormal(added, normal.X(), normal.Y(), normal.Z());
        }
        if (has_uvs) {
            double u = 0.0, v = 0.0;
            triangles->VertexTexel(vertex, u, v);
            result->SetVertexTexel(added, u, v);
        }
    };
    const int order[3] = {0, reflects ? 2 : 1, reflects ? 1 : 2};

    if (indexed) {
        for (int vertex = 1; vertex <= triangles->VertexNumber(); ++vertex) {
            copy_vertex(vertex);
        }
        for (int first = 1; first <= triangles->EdgeNumber(); first += 3) {
            result->AddEdges(triangles->Edge(first + order[0]), triangles->Edge(first + order[1]),
                             triangles->Edge(first + order[2]));
        }
    } else {
        for (int first = 1; first <= triangles->VertexNumber(); first += 3) {
            for (int corner : order) {
                copy_vertex(first + corner);
            }
        }
    }
    return result;
}

} // namespace

mtk::Mesh mtk::make_mesh(const mtk_viewer_mesh& mesh) {
    std::shared_ptr<Elements> elements;
    if (mesh.face_first_triangles != nullptr) {
        elements = std::make_shared<Elements>();
        elements->first_triangles.assign(mesh.face_first_triangles, mesh.face_first_triangles + mesh.face_count + 1);
        elements->edge_indices.assign(mesh.edge_indices, mesh.edge_indices + mesh.edge_count);
        for (int32_t vertex = 0; vertex < mesh.shape_vertex_count; ++vertex) {
            const double* xyz = mesh.shape_vertices + 3 * vertex;
            elements->vertices.emplace_back(xyz[0], xyz[1], xyz[2]);
        }
    }
    return Mesh{make_triangles(mesh), mesh.edge_first_points != nullptr, make_edges(mesh), std::move(elements)};
}

mtk::Mesh mtk::transform_mesh(const Mesh& mesh, const double* matrix) {
    std::shared_ptr<Elements> elements;
    if (mesh.elements) {
        elements = std::make_shared<Elements>(*mesh.elements);
        const gp_Mat linear = linear_part(matrix);
        const gp_XYZ translation(matrix[3], matrix[7], matrix[11]);
        for (gp_Pnt& vertex : elements->vertices) {
            vertex = gp_Pnt(vertex.XYZ().Multiplied(linear) + translation);
        }
    }
    return Mesh{transform_triangles(mesh.triangles, matrix), mesh.has_own_edges, transform_edges(mesh.edges, matrix),
                std::move(elements)};
}

occ::handle<Graphic3d_Texture2D> mtk::make_texture(const uint8_t* pixels, int width, int height) {
    occ::handle<Image_PixMap> image = new Image_PixMap();
    if (!image->InitTrash(Image_Format_RGBA, width, height)) {
        throw kernel_error("The memory for a texture could not be allocated.");
    }
    // Stored from the bottom row, as OpenCASCADE expects: ChangeRow counts rows from the top in either order.
    image->SetTopDown(false);
    const size_t row_size = static_cast<size_t>(width) * 4;
    for (int row = 0; row < height; ++row) {
        std::memcpy(image->ChangeRow(row), pixels + row * row_size, row_size);
    }

    occ::handle<Graphic3d_Texture2D> texture = new Graphic3d_Texture2D(image);
    texture->SetColorMap(true); // sRGB values, which the graphics card turns into linear ones
    texture->SetMipmapsGeneration(true);
    const occ::handle<Graphic3d_TextureParams>& parameters = texture->GetParams();
    parameters->SetModulate(true); // multiplied by the colour of the material, and lit
    parameters->SetRepeat(true);
    parameters->SetFilter(Graphic3d_TOTF_TRILINEAR);
    parameters->SetAnisoFilter(Graphic3d_LOTA_MIDDLE);
    return texture;
}

mtk::MaterialAspects mtk::make_aspects(const mtk_viewer_material& material,
                                       const occ::handle<Graphic3d_Texture2D>& texture) {
    // Faded, only the fronts of the triangles are drawn, so that what is behind shows through fewer layers.
    mtk_viewer_material faded = material;
    faded.base_color[3] *= faded_opacity;
    MaterialAspects aspects{make_aspect(material, texture), make_aspect(faded, texture), make_aspect(material, texture),
                            make_aspect(faded, texture)};
    aspects.faded->SetFaceCulling(Graphic3d_TypeOfBackfacingModel_BackCulled);
    aspects.faded_faces->SetFaceCulling(Graphic3d_TypeOfBackfacingModel_BackCulled);
    return aspects;
}

std::optional<gp_Trsf> mtk::to_placement(const double* matrix) {
    const gp_Mat linear = linear_part(matrix);
    const gp_XYZ columns[3] = {linear.Column(1), linear.Column(2), linear.Column(3)};
    const double lengths[3] = {columns[0].Modulus(), columns[1].Modulus(), columns[2].Modulus()};
    const double longest = std::max({lengths[0], lengths[1], lengths[2]});
    constexpr double tolerance = 1.0e-6;
    for (int i = 0; i < 3; ++i) {
        const int next = (i + 1) % 3;
        if (std::abs(lengths[i] - lengths[next]) > tolerance * longest ||
            std::abs(columns[i].Dot(columns[next])) > tolerance * longest * longest) {
            return std::nullopt;
        }
    }

    gp_Trsf placement;
    placement.SetValues(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5], matrix[6], matrix[7],
                        matrix[8], matrix[9], matrix[10], matrix[11]);
    return placement;
}
