#include "scene.hpp"

#include "mtk/errors.hpp"

#include <Graphic3d_Group.hxx>
#include <Graphic3d_MaterialAspect.hxx>
#include <Graphic3d_PBRMaterial.hxx>
#include <Graphic3d_TextureSet.hxx>
#include <Image_PixMap.hxx>
#include <Prs3d_Presentation.hxx>
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

} // namespace

mtk::MeshObject::MeshObject(const occ::handle<Graphic3d_ArrayOfTriangles>& triangles,
                            const occ::handle<Graphic3d_AspectFillArea3d>& aspect)
    : triangles_(triangles), aspect_(aspect) {}

void mtk::MeshObject::Compute(const occ::handle<PrsMgr_PresentationManager>&,
                              const occ::handle<Prs3d_Presentation>& presentation, const int mode) {
    if (mode != 0) {
        return;
    }
    const occ::handle<Graphic3d_Group> group = presentation->NewGroup();
    group->SetGroupPrimitivesAspect(aspect_);
    group->AddPrimitiveArray(triangles_);
}

occ::handle<Graphic3d_ArrayOfTriangles> mtk::make_triangles(const mtk_viewer_mesh& mesh) {
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

occ::handle<Graphic3d_ArrayOfTriangles> mtk::transform_triangles(
    const occ::handle<Graphic3d_ArrayOfTriangles>& triangles, const double* matrix) {
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

occ::handle<Graphic3d_AspectFillArea3d> mtk::make_aspect(const mtk_viewer_material& material,
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
