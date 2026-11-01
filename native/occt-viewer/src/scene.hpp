#pragma once

#include "mtk/occt_viewer.h"

#include <AIS_InteractiveObject.hxx>
#include <Graphic3d_ArrayOfTriangles.hxx>
#include <Graphic3d_AspectFillArea3d.hxx>
#include <Graphic3d_Texture2D.hxx>
#include <gp_Trsf.hxx>

#include <optional>

namespace mtk {

// A mesh of the scene with a material. Its presentation is computed once: the instances show it through an
// AIS_ConnectedInteractive each, which shares it, and so its buffers on the graphics card.
class MeshObject : public AIS_InteractiveObject {
    DEFINE_STANDARD_RTTI_INLINE(MeshObject, AIS_InteractiveObject)

public:
    MeshObject(const occ::handle<Graphic3d_ArrayOfTriangles>& triangles,
               const occ::handle<Graphic3d_AspectFillArea3d>& aspect);

    bool AcceptDisplayMode(const int mode) const override { return mode == 0; }

private:
    void Compute(const occ::handle<PrsMgr_PresentationManager>& manager,
                 const occ::handle<Prs3d_Presentation>& presentation, const int mode) override;

    // Meshes cannot be selected yet (#15).
    void ComputeSelection(const occ::handle<SelectMgr_Selection>&, const int) override {}

    occ::handle<Graphic3d_ArrayOfTriangles> triangles_;
    occ::handle<Graphic3d_AspectFillArea3d> aspect_;
};

// The triangles of a checked mesh; without normals, each triangle has its own, for flat shading.
occ::handle<Graphic3d_ArrayOfTriangles> make_triangles(const mtk_viewer_mesh& mesh);

// The triangles transformed by an affine matrix of 12 values, as in mtk_viewer_add_instance, which must be invertible.
occ::handle<Graphic3d_ArrayOfTriangles> transform_triangles(const occ::handle<Graphic3d_ArrayOfTriangles>& triangles,
                                                            const double* matrix);

// A texture from checked pixels, as in mtk_viewer_add_texture.
occ::handle<Graphic3d_Texture2D> make_texture(const uint8_t* pixels, int width, int height);

// The aspect of the triangles of a material; texture may be null.
occ::handle<Graphic3d_AspectFillArea3d> make_aspect(const mtk_viewer_material& material,
                                                    const occ::handle<Graphic3d_Texture2D>& texture);

// The matrix as a placement that OpenCASCADE can apply to a shared presentation: a rotation, possibly with a
// reflection, a uniform scale and a translation. Other affine matrices have no such placement.
std::optional<gp_Trsf> to_placement(const double* matrix);

} // namespace mtk
