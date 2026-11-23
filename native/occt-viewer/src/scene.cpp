#include "scene.hpp"

#include "mtk/errors.hpp"

#include <Graphic3d_ArrayOfPoints.hxx>
#include <Graphic3d_AspectMarker3d.hxx>
#include <Graphic3d_Camera.hxx>
#include <Graphic3d_Group.hxx>
#include <Graphic3d_IndexBuffer.hxx>
#include <Graphic3d_BoundBuffer.hxx>
#include <Graphic3d_MaterialAspect.hxx>
#include <Graphic3d_PBRMaterial.hxx>
#include <Graphic3d_ShaderObject.hxx>
#include <Graphic3d_ShaderProgram.hxx>
#include <Graphic3d_TextureSet.hxx>
#include <Image_PixMap.hxx>
#include <Prs3d_LineAspect.hxx>
#include <Prs3d_Presentation.hxx>
#include <PrsMgr_PresentationManager.hxx>
#include <Select3D_SensitivePoint.hxx>
#include <Select3D_SensitivePoly.hxx>
#include <Select3D_SensitivePrimitiveArray.hxx>
#include <SelectMgr_EntityOwner.hxx>
#include <SelectMgr_SelectingVolumeManager.hxx>
#include <SelectMgr_Selection.hxx>
#include <gp_Mat.hxx>

#include <algorithm>
#include <cstdlib>
#include <cmath>
#include <cstring>
#include <map>
#include <tuple>

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

// In the CAD render style the faces have no highlights: a face turned to the light of the viewer gets about the whole
// colour of its material, one turned away from it a little more than half (see Viewer::apply_lights).
constexpr float cad_ambient = 0.55f;
constexpr float cad_diffuse = 0.5f;

Quantity_Color scaled(const Quantity_Color& color, float factor) {
    return Quantity_Color(color.Red() * factor, color.Green() * factor, color.Blue() * factor, Quantity_TOC_RGB);
}

Graphic3d_MaterialAspect make_material(const mtk_viewer_material& material, mtk_viewer_render_style style) {
    const Quantity_Color color(material.base_color[0], material.base_color[1], material.base_color[2],
                               Quantity_TOC_RGB);
    const float opacity = material.base_color[3];

    Graphic3d_MaterialAspect aspect_material(Graphic3d_NameOfMaterial_UserDefined);
    if (style == MTK_VIEWER_RENDER_CAD) {
        aspect_material.SetAmbientColor(scaled(color, cad_ambient));
        aspect_material.SetDiffuseColor(scaled(color, cad_diffuse));
        aspect_material.SetSpecularColor(Quantity_NOC_BLACK);
        aspect_material.SetEmissiveColor(Quantity_NOC_BLACK);
        aspect_material.SetShininess(0.0f);
    } else {
        // The view shades as OpenCASCADE does the metallic-roughness materials of glTF without physically based
        // rendering (XCAFDoc_VisMaterial): the metallic factor becomes the specular colour, the roughness the inverse
        // of the shininess.
        aspect_material.SetColor(color);
        aspect_material.SetSpecularColor(
            Quantity_Color(material.metallic, material.metallic, material.metallic, Quantity_TOC_RGB));
        aspect_material.SetShininess(1.0f - material.roughness);
    }
    aspect_material.SetTransparency(1.0f - opacity);
    // The material keeps them too, for a physically based shading.
    Graphic3d_PBRMaterial physical;
    physical.SetColor(Quantity_ColorRGBA(color, opacity));
    physical.SetMetallic(material.metallic);
    physical.SetRoughness(material.roughness);
    aspect_material.SetPBRMaterial(physical);
    return aspect_material;
}

void apply_render_style_to(Graphic3d_AspectFillArea3d& aspect, const mtk_viewer_material& material,
                           mtk_viewer_render_style style) {
    const Graphic3d_MaterialAspect aspect_material = make_material(material, style);
    aspect.SetFrontMaterial(aspect_material);
    aspect.SetBackMaterial(aspect_material);
}

occ::handle<Graphic3d_AspectFillArea3d> make_aspect(const mtk_viewer_material& material,
                                                    const occ::handle<Graphic3d_Texture2D>& texture,
                                                    mtk_viewer_render_style style) {
    const float opacity = material.base_color[3];
    occ::handle<Graphic3d_AspectFillArea3d> aspect = new Graphic3d_AspectFillArea3d();
    aspect->SetInteriorStyle(Aspect_IS_SOLID);
    apply_render_style_to(*aspect, material, style);
    if (opacity < 1.0f) {
        aspect->SetAlphaMode(Graphic3d_AlphaMode_Blend);
    }
    if (!texture.IsNull()) {
        aspect->SetTextureSet(new Graphic3d_TextureSet(texture));
        aspect->SetTextureMapOn(true);
    }
    return aspect;
}

// OpenGL draws lines one pixel wide at most in its core profile, as on macOS: a geometry shader draws the outlines
// instead as quads of a width in pixels. OpenCASCADE does not give the width of lines to the shaders of line aspects
// (occLineWidth stays 0), so each width has a program of its own. The outlines are drawn a little closer to the camera
// than they are, a fraction of their distance, so that the faces they bound do not cover half of them; a depth offset
// would not do, as the depth of perspective is far from linear. With caps, each segment goes on beyond its ends by half
// the width, so that the corners where segments meet are filled. With a dash length, in pixels, the segments are
// dashed, each from its start.
occ::handle<Graphic3d_ShaderProgram> make_outline_program(float width, bool caps, float dash) {
    occ::handle<Graphic3d_ShaderProgram> program = new Graphic3d_ShaderProgram();
    program->SetId(TCollection_AsciiString(caps ? "mtk_outlines_" : "mtk_lines_") +
                   TCollection_AsciiString(static_cast<int>(width * 100)) + "_" +
                   TCollection_AsciiString(static_cast<int>(dash * 100)));
    program->SetHeader("#version 150");
    program->SetNbLightsMax(0);
    program->SetNbClipPlanesMax(0);
    program->AttachShader(Graphic3d_ShaderObject::CreateFromSource(Graphic3d_TOS_VERTEX, R"(
void main() {
    vec4 position = occWorldViewMatrix * occModelWorldMatrix * occVertex;
    position.z += 0.002 * abs(position.z);
    gl_Position = occProjectionMatrix * position;
})"));
    program->AttachShader(Graphic3d_ShaderObject::CreateFromSource(
        Graphic3d_TOS_GEOMETRY, TCollection_AsciiString("const float width = ") + TCollection_AsciiString(width) +
                                    "; const float cap = " + (caps ? "0.5" : "0.0") + ";" + R"(
layout(lines) in;
layout(triangle_strip, max_vertices = 4) out;
uniform vec4 occViewport;
noperspective out float along_pixels; // the distance from the start of the segment, for dashes, linear on screen
void main() {
    vec4 start = gl_in[0].gl_Position;
    vec4 end = gl_in[1].gl_Position;
    vec2 half_size = 0.5 * occViewport.zw;
    vec2 along = (end.xy / end.w - start.xy / start.w) * half_size;
    float length = max(length(along), 1.0e-6);
    // Half the width across the segment, and the cap beyond its ends.
    vec2 forward = along / length * cap * width / half_size;
    vec2 across = vec2(-along.y, along.x) / length * 0.5 * width / half_size;
    along_pixels = -cap * width;
    gl_Position = vec4(start.xy + (-forward + across) * start.w, start.zw);
    EmitVertex();
    gl_Position = vec4(start.xy + (-forward - across) * start.w, start.zw);
    EmitVertex();
    along_pixels = length + cap * width;
    gl_Position = vec4(end.xy + (forward + across) * end.w, end.zw);
    EmitVertex();
    gl_Position = vec4(end.xy + (forward - across) * end.w, end.zw);
    EmitVertex();
    EndPrimitive();
})"));
    program->AttachShader(Graphic3d_ShaderObject::CreateFromSource(
        Graphic3d_TOS_FRAGMENT, TCollection_AsciiString("const float dash = ") + TCollection_AsciiString(dash) + ";" +
                                    R"(
noperspective in float along_pixels;
void main() {
    // Dashes as long as the gaps between them.
    if (dash > 0.0 && mod(along_pixels, 2.0 * dash) > dash) {
        discard;
    }
    occSetFragColor(occColor);
})"));
    return program;
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
    aspect.SetDrawEdges(triangle_edges);
    aspect.SetEdgeColor(edges);
    aspect.SetEdgeWidth(1.0);
}

} // namespace

void mtk::apply_display_mode(MaterialAspects& aspects, mtk_viewer_display_mode mode, const EdgeStyle& edges) {
    const Quantity_ColorRGBA shaded(edges.color, 1.0f), faded(edges.color, faded_opacity);
    apply_display_mode_to(*aspects.shaded, mode, shaded, edges.triangle_edges);
    apply_display_mode_to(*aspects.faded, mode, faded, edges.triangle_edges);
    apply_display_mode_to(*aspects.shaded_faces, mode, shaded, false);
    apply_display_mode_to(*aspects.faded_faces, mode, faded, false);
    // The faces are drawn deeper than they are by as much as their depth changes over the pixels that the edges they
    // bound cover beside them, half their width across and as much beyond their ends, so that they do not hide half
    // of those edges; and by two steps of the depth more, for the edges on faces turned to the camera. A face turned
    // to the camera is thus hardly moved, and what is behind it, however close, stays hidden; a face seen edge-on is
    // moved by a few pixels of its slope, as little as the edges need.
    const float width = edges.outlines ? edges.outline_width : 1.0f;
    for (const occ::handle<Graphic3d_AspectFillArea3d>& aspect :
         {aspects.shaded, aspects.faded, aspects.shaded_faces, aspects.faded_faces}) {
        aspect->SetPolygonOffsets(Aspect_POM_Fill, 0.75f * width + 1.0f, 2.0f);
    }
}

// The edges of meshes, as quads made by edge_quads_of(): each vertex is an end of a segment, its normal the other end
// and the first coordinate of its texel the side of the segment it goes to. The vertex shader widens the quad on
// screen, as the outline program does with a geometry shader, without one: Metal has none, and the OpenGL of macOS
// draws each array with one in a render pass of its own.
occ::handle<Graphic3d_ShaderProgram> make_edge_program(float width) {
    occ::handle<Graphic3d_ShaderProgram> program = new Graphic3d_ShaderProgram();
    program->SetId(TCollection_AsciiString("mtk_edges_") + TCollection_AsciiString(static_cast<int>(width * 100)));
    program->SetHeader("#version 150");
    program->SetNbLightsMax(0);
    program->SetNbClipPlanesMax(0);
    program->AttachShader(Graphic3d_ShaderObject::CreateFromSource(
        Graphic3d_TOS_VERTEX, TCollection_AsciiString("const float width = ") + TCollection_AsciiString(width) + ";" +
                                  R"(
uniform vec4 occViewport;
vec4 project(vec4 point) {
    return occProjectionMatrix * occWorldViewMatrix * occModelWorldMatrix * point;
}
void main() {
    vec4 here = project(occVertex);
    vec4 there = project(vec4(occNormal, 1.0));
    vec2 half_size = 0.5 * occViewport.zw;
    vec2 along = (there.xy / there.w - here.xy / here.w) * half_size;
    vec2 direction = along / max(length(along), 1.0e-6);
    // Half the width across, and as much beyond this end, away from the other, to fill the corners.
    vec2 offset = (vec2(-direction.y, direction.x) * occTexCoord.x - direction) * 0.5 * width / half_size;
    gl_Position = vec4(here.xy + offset * here.w, here.zw);
})"));
    program->AttachShader(Graphic3d_ShaderObject::CreateFromSource(Graphic3d_TOS_FRAGMENT, R"(
void main() {
    occSetFragColor(occColor);
})"));
    return program;
}

occ::handle<Graphic3d_ShaderProgram> edge_program(float width) {
    static std::map<float, occ::handle<Graphic3d_ShaderProgram>> programs;
    occ::handle<Graphic3d_ShaderProgram>& program = programs[width];
    if (program.IsNull()) {
        program = make_edge_program(width);
    }
    return program;
}

occ::handle<Graphic3d_ShaderProgram> mtk::outline_program(float width, bool caps, float dash) {
    static std::map<std::tuple<float, bool, float>, occ::handle<Graphic3d_ShaderProgram>> programs;
    occ::handle<Graphic3d_ShaderProgram>& program = programs[{width, caps, dash}];
    if (program.IsNull()) {
        program = make_outline_program(width, caps, dash);
    }
    return program;
}

mtk::EdgeAspects mtk::make_edge_aspects() {
    EdgeAspects aspects{new Graphic3d_AspectFillArea3d(), new Graphic3d_AspectFillArea3d(),
                        new Graphic3d_AspectFillArea3d(), new Graphic3d_AspectFillArea3d()};
    for (const occ::handle<Graphic3d_AspectFillArea3d>& aspect :
         {aspects.shaded, aspects.faded, aspects.lines, aspects.faded_lines}) {
        aspect->SetShadingModel(Graphic3d_TypeOfShadingModel_Unlit);
        aspect->SetFaceCulling(Graphic3d_TypeOfBackfacingModel_DoubleSided);
        aspect->SetDrawEdges(false);
    }
    aspects.faded->SetAlphaMode(Graphic3d_AlphaMode_Blend);
    aspects.faded_lines->SetAlphaMode(Graphic3d_AlphaMode_Blend);
    return aspects;
}

void mtk::apply_display_mode(EdgeAspects& aspects, const EdgeStyle& edges) {
    // An empty interior draws nothing: the same presentations serve all the display modes. The edges are as wide as
    // the outlines, or one pixel.
    const occ::handle<Graphic3d_ShaderProgram> program = edge_program(edges.outlines ? edges.outline_width : 1.0f);
    for (const occ::handle<Graphic3d_AspectFillArea3d>& aspect : {aspects.shaded, aspects.faded}) {
        aspect->SetInteriorStyle(edges.own_edges ? Aspect_IS_SOLID : Aspect_IS_EMPTY);
        aspect->SetShaderProgram(program);
    }
    for (const occ::handle<Graphic3d_AspectFillArea3d>& aspect :
         {aspects.shaded, aspects.faded, aspects.lines, aspects.faded_lines}) {
        aspect->SetPolygonOffsets(Aspect_POM_None, 0.0f, 0.0f); // at their depth: the faces go deeper instead
    }
    for (const occ::handle<Graphic3d_AspectFillArea3d>& aspect : {aspects.lines, aspects.faded_lines}) {
        aspect->SetInteriorStyle(edges.lines ? Aspect_IS_SOLID : Aspect_IS_EMPTY);
        aspect->SetShaderProgram(program);
    }
    aspects.shaded->SetInteriorColor(Quantity_ColorRGBA(edges.color, 1.0f));
    aspects.faded->SetInteriorColor(Quantity_ColorRGBA(edges.color, faded_opacity));
    aspects.lines->SetInteriorColor(Quantity_ColorRGBA(edges.color, 1.0f));
    aspects.faded_lines->SetInteriorColor(Quantity_ColorRGBA(edges.color, faded_opacity));
}

mtk::MeshObject::MeshObject(const Mesh& mesh, const MaterialAspects& aspects, const EdgeAspects& edge_aspects,
                            const std::vector<MaterialAspects>& materials)
    : mesh_(mesh), aspects_(aspects), edge_aspects_(edge_aspects), materials_(&materials) {}

void mtk::MeshObject::Compute(const occ::handle<PrsMgr_PresentationManager>&,
                              const occ::handle<Prs3d_Presentation>& presentation, const int mode) {
    if (!AcceptDisplayMode(mode)) {
        return;
    }
    const bool faded = mode == faded_mode;
    if (mesh_.triangles.IsNull()) {
        // Edges only.
    } else if (mesh_.face_materials.empty() || !mesh_.elements) {
        const occ::handle<Graphic3d_Group> faces = presentation->NewGroup();
        if (mesh_.has_own_edges) {
            faces->SetGroupPrimitivesAspect(faded ? aspects_.faded_faces : aspects_.shaded_faces);
        } else {
            faces->SetGroupPrimitivesAspect(faded ? aspects_.faded : aspects_.shaded);
        }
        faces->AddPrimitiveArray(mesh_.triangles);
    } else {
        // A group per material, of the triangles of its faces, which share the vertices of the mesh. Made from the
        // triangles as they are, as those of a reflected copy have their corners swapped.
        std::map<int32_t, std::vector<int32_t>> triangles_of;
        const std::vector<int32_t>& first = mesh_.elements->first_triangles;
        for (size_t face = 0; face < mesh_.face_materials.size(); ++face) {
            std::vector<int32_t>& triangles = triangles_of[mesh_.face_materials[face]];
            for (int32_t triangle = first[face]; triangle < first[face + 1]; ++triangle) {
                triangles.push_back(triangle);
            }
        }
        const Graphic3d_ArrayOfTriangles& all = *mesh_.triangles;
        const bool indexed = all.EdgeNumber() > 0;
        for (const auto& [material, triangles] : triangles_of) {
            if (triangles.empty()) {
                continue;
            }
            const MaterialAspects& aspects = material < 0 ? aspects_ : (*materials_)[material];
            occ::handle<Graphic3d_IndexBuffer> indices = new Graphic3d_IndexBuffer(Graphic3d_Buffer::DefaultAllocator());
            if (!indices->Init<int32_t>(static_cast<int>(3 * triangles.size()))) {
                throw kernel_error("The memory for the triangles of a material could not be allocated.");
            }
            int index = 0;
            for (const int32_t triangle : triangles) {
                for (int corner = 1; corner <= 3; ++corner) {
                    const int rank = 3 * triangle + corner; // from 1
                    indices->SetIndex(index++, (indexed ? all.Edge(rank) : rank) - 1);
                }
            }
            const occ::handle<Graphic3d_Group> faces = presentation->NewGroup();
            faces->SetGroupPrimitivesAspect(faded ? aspects.faded_faces : aspects.shaded_faces);
            faces->AddPrimitiveArray(Graphic3d_TOPA_TRIANGLES, indices, all.Attributes(), occ::handle<Graphic3d_BoundBuffer>());
        }
    }

    if (!mesh_.edge_quads.IsNull()) {
        const occ::handle<Graphic3d_Group> edges = presentation->NewGroup();
        if (mesh_.triangles.IsNull()) {
            edges->SetGroupPrimitivesAspect(faded ? edge_aspects_.faded_lines : edge_aspects_.lines);
        } else {
            edges->SetGroupPrimitivesAspect(faded ? edge_aspects_.faded : edge_aspects_.shaded);
        }
        edges->AddPrimitiveArray(mesh_.edge_quads);
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
    if (mesh_.triangles.IsNull()) {
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
constexpr double highlighted_vertex_scale = 3.0;

// Near an edge or a vertex, OpenCASCADE keeps the closest of the face and the edge, whatever their priorities, as
// edges and vertices have no normal (SelectMgr_SortCriterion::IsCloserDepth). Faces are therefore picked as if
// farther by this part of the height of the view at their depth, about as many pixels as the pick tolerance, so that
// the edges and vertices on them win, while those hidden behind them still lose.
constexpr double face_depth_offset = 0.01;

class FaceSensitive : public Select3D_SensitivePrimitiveArray {
    DEFINE_STANDARD_RTTI_INLINE(FaceSensitive, Select3D_SensitivePrimitiveArray)

public:
    using Select3D_SensitivePrimitiveArray::Select3D_SensitivePrimitiveArray;

    bool Matches(SelectBasics_SelectingVolumeManager& manager, SelectBasics_PickResult& result) override {
        if (!Select3D_SensitivePrimitiveArray::Matches(manager, result)) {
            return false;
        }
        auto* volumes = dynamic_cast<SelectMgr_SelectingVolumeManager*>(&manager);
        if (volumes == nullptr || volumes->GetActiveSelectionType() != SelectMgr_SelectionType_Point ||
            volumes->Camera().IsNull()) {
            return true;
        }
        if (!result.HasPickedPoint()) {
            result.SetPickedPoint(volumes->DetectedPoint(result.Depth())); // the point picked, not the moved one
        }
        // The depth counts from the near plane, in the units of the scene, whatever the placement of the object. The
        // size of the window is not known here: the volumes of an object with a placement are a transformed copy.
        const Graphic3d_Camera& camera = *volumes->Camera();
        const double view_height = camera.IsOrthographic()
                                       ? camera.ViewDimensions().Y()
                                       : camera.ViewDimensions(camera.ZNear() + result.Depth()).Y();
        result.SetDepth(result.Depth() + face_depth_offset * view_height);
        return true;
    }
};

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

void mtk::ElementOwner::draw(const occ::handle<Prs3d_Presentation>& presentation, const Prs3d_Drawer& style,
                             float opacity) const {
    const Quantity_Color color = style.Color();
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
        occ::handle<Graphic3d_AspectLine3d> aspect = new Graphic3d_AspectLine3d(color, Aspect_TOL_SOLID, 1.0);
        // Without caps, which would go on beyond the vertices at the ends of the edge.
        aspect->SetShaderProgram(outline_program(static_cast<float>(style.LineAspect()->Aspect()->Width()), false));
        group->SetGroupPrimitivesAspect(aspect);
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
    draw(hover_, *style, hovered_face_opacity);
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
    draw(selected_, *style, selected_face_opacity);
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
        occ::handle<Select3D_SensitivePrimitiveArray> sensitive =
            new FaceSensitive(new ElementOwner(object, mesh, MTK_VIEWER_ELEMENT_FACE, static_cast<int>(face)));
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
    if (mesh.triangle_count == 0) {
        return nullptr;
    }
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

// The quads that draw the segments of polylines in one call, as edge_program() expects them; null for none.
occ::handle<Graphic3d_ArrayOfTriangles> edge_quads_of(const occ::handle<Graphic3d_ArrayOfPolylines>& polylines) {
    if (polylines.IsNull() || polylines->VertexNumber() < 2) {
        return nullptr;
    }
    const int bounds = std::max(polylines->BoundNumber(), 1);
    const int segment_count = polylines->VertexNumber() - bounds;
    if (segment_count <= 0) {
        return nullptr;
    }
    occ::handle<Graphic3d_ArrayOfTriangles> quads = new Graphic3d_ArrayOfTriangles(
        4 * segment_count, 6 * segment_count, Graphic3d_ArrayFlags_VertexNormal | Graphic3d_ArrayFlags_VertexTexel);
    const auto add = [&](const gp_Pnt& here, const gp_Pnt& there, double side) {
        const int vertex = quads->AddVertex(here);
        // Not normalized: the normal holds the other end of the segment.
        quads->SetVertexNormal(vertex, there.X(), there.Y(), there.Z());
        quads->SetVertexTexel(vertex, gp_Pnt2d(side, 0.0));
        return vertex;
    };
    int first = 1;
    for (int bound = 1; bound <= bounds; ++bound) {
        const int count = polylines->BoundNumber() > 0 ? polylines->Bound(bound) : polylines->VertexNumber();
        for (int vertex = first; vertex + 1 < first + count; ++vertex) {
            const gp_Pnt start = polylines->Vertice(vertex), end = polylines->Vertice(vertex + 1);
            // Across the segment, the side of each end is seen from the other: the end flips it.
            const int a = add(start, end, 1.0), b = add(start, end, -1.0);
            const int c = add(end, start, -1.0), d = add(end, start, 1.0);
            quads->AddEdges(a, b, c);
            quads->AddEdges(b, d, c);
        }
        first += count;
    }
    return quads;
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
    std::vector<int32_t> face_materials;
    if (elements && mesh.face_materials != nullptr) {
        face_materials.assign(mesh.face_materials, mesh.face_materials + mesh.face_count);
    }
    Mesh result{true, make_triangles(mesh), mesh.edge_first_points != nullptr, make_edges(mesh), std::move(elements),
                std::move(face_materials), nullptr};
    result.edge_quads = edge_quads_of(result.edges);
    return result;
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
    Mesh result{true, mesh.triangles.IsNull() ? nullptr : transform_triangles(mesh.triangles, matrix),
                mesh.has_own_edges, transform_edges(mesh.edges, matrix), std::move(elements), mesh.face_materials, nullptr};
    result.edge_quads = edge_quads_of(result.edges);
    return result;
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
                                       const occ::handle<Graphic3d_Texture2D>& texture,
                                       mtk_viewer_render_style style) {
    // Faded, only the fronts of the triangles are drawn, so that what is behind shows through fewer layers.
    mtk_viewer_material faded = material;
    faded.base_color[3] *= faded_opacity;
    MaterialAspects aspects{make_aspect(material, texture, style), make_aspect(faded, texture, style),
                            make_aspect(material, texture, style), make_aspect(faded, texture, style), material};
    aspects.faded->SetFaceCulling(Graphic3d_TypeOfBackfacingModel_BackCulled);
    aspects.faded_faces->SetFaceCulling(Graphic3d_TypeOfBackfacingModel_BackCulled);
    return aspects;
}

void mtk::apply_render_style(MaterialAspects& aspects, mtk_viewer_render_style style) {
    mtk_viewer_material faded = aspects.material;
    faded.base_color[3] *= faded_opacity;
    apply_render_style_to(*aspects.shaded, aspects.material, style);
    apply_render_style_to(*aspects.faded, faded, style);
    apply_render_style_to(*aspects.shaded_faces, aspects.material, style);
    apply_render_style_to(*aspects.faded_faces, faded, style);
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
