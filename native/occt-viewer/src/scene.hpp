#pragma once

#include "mtk/occt_viewer.h"

#include <AIS_InteractiveObject.hxx>
#include <Graphic3d_ArrayOfPolylines.hxx>
#include <Graphic3d_ArrayOfTriangles.hxx>
#include <Graphic3d_AspectLine3d.hxx>
#include <Graphic3d_AspectFillArea3d.hxx>
#include <Graphic3d_Texture2D.hxx>
#include <gp_Trsf.hxx>

#include <optional>

namespace mtk {

// A mesh as the viewer keeps it: its triangles, and the polylines of the edges of its own, if it has any.
struct Mesh {
    occ::handle<Graphic3d_ArrayOfTriangles> triangles;
    bool has_own_edges = false;               // otherwise the display modes with edges draw those of the triangles
    occ::handle<Graphic3d_ArrayOfPolylines> edges; // null when it has none
};

// The aspects of the triangles of a material: shaded, and faded around a selection; with the edges of the triangles in
// the display modes that draw edges, or only faces, for the meshes with edges of their own.
struct MaterialAspects {
    occ::handle<Graphic3d_AspectFillArea3d> shaded;
    occ::handle<Graphic3d_AspectFillArea3d> faded;
    occ::handle<Graphic3d_AspectFillArea3d> shaded_faces;
    occ::handle<Graphic3d_AspectFillArea3d> faded_faces;
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

    MeshObject(const Mesh& mesh, const MaterialAspects& aspects, const EdgeAspects& edge_aspects);

    bool AcceptDisplayMode(const int mode) const override { return mode == shaded_mode || mode == faded_mode; }

private:
    void Compute(const occ::handle<PrsMgr_PresentationManager>& manager,
                 const occ::handle<Prs3d_Presentation>& presentation, const int mode) override;

    // Picked as a whole, by any of its triangles.
    void ComputeSelection(const occ::handle<SelectMgr_Selection>& selection, const int mode) override;

    Mesh mesh_;
    MaterialAspects aspects_;
    EdgeAspects edge_aspects_;
};

// A checked mesh; without normals, each triangle has its own, for flat shading.
Mesh make_mesh(const mtk_viewer_mesh& mesh);

// The mesh transformed by an affine matrix of 12 values, as in mtk_viewer_add_instance, which must be invertible.
Mesh transform_mesh(const Mesh& mesh, const double* matrix);

// A texture from checked pixels, as in mtk_viewer_add_texture.
occ::handle<Graphic3d_Texture2D> make_texture(const uint8_t* pixels, int width, int height);

// The aspects of the triangles of a checked material, shaded; texture may be null.
MaterialAspects make_aspects(const mtk_viewer_material& material, const occ::handle<Graphic3d_Texture2D>& texture);

// Changes the aspects to a display mode, with edges of the given colour where it draws them. The presentations that use
// the aspects show the change once their aspects are synchronized (PrsMgr_PresentableObject::SynchronizeAspects).
void apply_display_mode(MaterialAspects& aspects, mtk_viewer_display_mode mode, const Quantity_Color& edges);

EdgeAspects make_edge_aspects();
void apply_display_mode(EdgeAspects& aspects, mtk_viewer_display_mode mode, const Quantity_Color& edges);

// The matrix as a placement that OpenCASCADE can apply to a shared presentation: a rotation, possibly with a
// reflection, a uniform scale and a translation. Other affine matrices have no such placement.
std::optional<gp_Trsf> to_placement(const double* matrix);

} // namespace mtk
