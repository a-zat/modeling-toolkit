#pragma once

#include "mtk/occt_viewer.h"

#include <AIS_ConnectedInteractive.hxx>
#include <AIS_InteractiveObject.hxx>
#include <Graphic3d_ArrayOfPolylines.hxx>
#include <Graphic3d_ArrayOfTriangles.hxx>
#include <Graphic3d_AspectLine3d.hxx>
#include <Graphic3d_AspectFillArea3d.hxx>
#include <Graphic3d_ShaderProgram.hxx>
#include <Graphic3d_Texture2D.hxx>
#include <Prs3d_Drawer.hxx>
#include <Prs3d_Presentation.hxx>
#include <SelectMgr_EntityOwner.hxx>
#include <gp_Pnt.hxx>
#include <gp_Trsf.hxx>

#include <memory>
#include <optional>
#include <vector>

namespace mtk {

// The faces, edges and vertices of the shape a mesh is the tessellation of, numbered as in the shape.
struct Elements {
    std::vector<int32_t> first_triangles; // of each face, then the number of triangles
    std::vector<int32_t> edge_indices;    // in the shape, of each polyline of the edges of the mesh
    std::vector<gp_Pnt> vertices;
};

// A mesh as the viewer keeps it: its triangles, and the polylines of the edges of its own, if it has any.
struct Mesh {
    occ::handle<Graphic3d_ArrayOfTriangles> triangles;
    bool has_own_edges = false;               // otherwise the display modes with edges draw those of the triangles
    occ::handle<Graphic3d_ArrayOfPolylines> edges; // null when it has none
    std::shared_ptr<const Elements> elements;      // null for a mesh picked as a whole
    // With elements, the material of each face, -1 for the one of the instance; empty for that one everywhere.
    std::vector<int32_t> face_materials;
};

// The owner of a face, an edge or a vertex of the shape that an object shows, picked with priority to vertices, then
// edges, then faces. It draws its element when highlighted: hovered, by the context, or selected, by the viewer
// (highlight_selected), each in a presentation of its own, so that several elements of an object can be selected.
class ElementOwner : public SelectMgr_EntityOwner {
    DEFINE_STANDARD_RTTI_INLINE(ElementOwner, SelectMgr_EntityOwner)

public:
    // polyline: for an edge, its polyline in the edges of the mesh; index: the element in the shape.
    ElementOwner(const occ::handle<SelectMgr_SelectableObject>& object, const Mesh& mesh,
                 mtk_viewer_element_kind kind, int index, int polyline = -1);

    mtk_viewer_element_kind kind() const { return kind_; }
    int index() const { return index_; }

    void HilightWithColor(const occ::handle<PrsMgr_PresentationManager>& manager,
                          const occ::handle<Prs3d_Drawer>& style, const int mode) override;

    // The hover only: the selected element stays highlighted until unhighlight_selected.
    void Unhilight(const occ::handle<PrsMgr_PresentationManager>& manager, const int mode) override;

    void highlight_selected(const occ::handle<PrsMgr_PresentationManager>& manager,
                            const occ::handle<Prs3d_Drawer>& style);
    void unhighlight_selected();

private:
    // In the colour of the style; edges as wide as its lines, in pixels.
    void draw(const occ::handle<Prs3d_Presentation>& presentation, const Prs3d_Drawer& style, float opacity) const;

    Mesh mesh_;
    mtk_viewer_element_kind kind_;
    int index_;
    int polyline_;
    occ::handle<Prs3d_Presentation> hover_;
    occ::handle<Prs3d_Presentation> selected_;
};

// Adds to a selection the faces, edges and vertices of a mesh with elements, with owners for the given object, which
// shows the mesh.
void add_element_sensitives(const occ::handle<SelectMgr_Selection>& selection,
                            const occ::handle<SelectMgr_SelectableObject>& object, const Mesh& mesh);

// The aspects of the triangles of a material: shaded, and faded around a selection; with the edges of the triangles in
// the display modes that draw edges, or only faces, for the meshes with edges of their own. The material they were made
// from shades them again in another render style.
struct MaterialAspects {
    occ::handle<Graphic3d_AspectFillArea3d> shaded;
    occ::handle<Graphic3d_AspectFillArea3d> faded;
    occ::handle<Graphic3d_AspectFillArea3d> shaded_faces;
    occ::handle<Graphic3d_AspectFillArea3d> faded_faces;
    mtk_viewer_material material;
};

// The aspects of the edges of their own of the meshes, shaded and faded: hidden in the display mode without edges.
struct EdgeAspects {
    occ::handle<Graphic3d_AspectLine3d> shaded;
    occ::handle<Graphic3d_AspectLine3d> faded;
};

// A mesh of the scene with a material, in two display modes: shaded, and faded around a selection. Each presentation
// is computed once, the faded one when first needed: the instances show them through an AIS_ConnectedInteractive
// each, which shares them, and so their buffers on the graphics card.
class MeshObject : public AIS_InteractiveObject {
    DEFINE_STANDARD_RTTI_INLINE(MeshObject, AIS_InteractiveObject)

public:
    static constexpr int shaded_mode = 0;
    static constexpr int faded_mode = 1;

    // materials: those of the viewer, which the faces with materials of their own take, kept by the viewer.
    MeshObject(const Mesh& mesh, const MaterialAspects& aspects, const EdgeAspects& edge_aspects,
               const std::vector<MaterialAspects>& materials);

    bool AcceptDisplayMode(const int mode) const override { return mode == shaded_mode || mode == faded_mode; }

    const Mesh& mesh() const { return mesh_; }

private:
    void Compute(const occ::handle<PrsMgr_PresentationManager>& manager,
                 const occ::handle<Prs3d_Presentation>& presentation, const int mode) override;

    // Picked as a whole, by any of its triangles, unless the mesh has elements.
    void ComputeSelection(const occ::handle<SelectMgr_Selection>& selection, const int mode) override;

    Mesh mesh_;
    MaterialAspects aspects_;
    EdgeAspects edge_aspects_;
    const std::vector<MaterialAspects>* materials_;
};

// A mesh object placed by a transformation, sharing its presentations. Unlike AIS_ConnectedInteractive, whose owners
// lose what they own, the faces, edges and vertices of the mesh keep owners of their own, for this object.
class ConnectedMesh : public AIS_ConnectedInteractive {
    DEFINE_STANDARD_RTTI_INLINE(ConnectedMesh, AIS_ConnectedInteractive)

    void ComputeSelection(const occ::handle<SelectMgr_Selection>& selection, const int mode) override;
};

// A checked mesh; without normals, each triangle has its own, for flat shading.
Mesh make_mesh(const mtk_viewer_mesh& mesh);

// The mesh transformed by an affine matrix of 12 values, as in mtk_viewer_add_instance, which must be invertible.
Mesh transform_mesh(const Mesh& mesh, const double* matrix);

// A texture from checked pixels, as in mtk_viewer_add_texture.
occ::handle<Graphic3d_Texture2D> make_texture(const uint8_t* pixels, int width, int height);

// The aspects of the triangles of a checked material, shaded in a render style; texture may be null.
MaterialAspects make_aspects(const mtk_viewer_material& material, const occ::handle<Graphic3d_Texture2D>& texture,
                             mtk_viewer_render_style style);

// Shades the aspects in a render style; as for apply_display_mode, the presentations show it once synchronized.
void apply_render_style(MaterialAspects& aspects, mtk_viewer_render_style style);

// Which edges the display mode and the render style draw, and how.
struct EdgeStyle {
    bool triangle_edges = false; // those of the triangles, for the meshes without edges of their own
    bool own_edges = false;      // the edges of their own of the meshes
    bool outlines = false;       // drawn thick, over the faces, as in the CAD render style
    float outline_width = 1.0f;  // in pixels
    Quantity_Color color;
};

// Changes the aspects to a display mode, with the edges of the triangles as the style says. The presentations that use
// the aspects show the change once their aspects are synchronized (PrsMgr_PresentableObject::SynchronizeAspects).
void apply_display_mode(MaterialAspects& aspects, mtk_viewer_display_mode mode, const EdgeStyle& edges);

EdgeAspects make_edge_aspects();

// The program of the line aspects that draws their lines a width in pixels, thick also where OpenGL draws them one pixel
// wide at most, and a little closer to the camera than they are, over the faces they bound. Caps fill the corners of
// lines that meet, going on beyond their ends by half the width.
occ::handle<Graphic3d_ShaderProgram> outline_program(float width, bool caps = true);
void apply_display_mode(EdgeAspects& aspects, const EdgeStyle& edges);

// The matrix as a placement that OpenCASCADE can apply to a shared presentation: a rotation, possibly with a
// reflection, a uniform scale and a translation. Other affine matrices have no such placement.
std::optional<gp_Trsf> to_placement(const double* matrix);

} // namespace mtk
