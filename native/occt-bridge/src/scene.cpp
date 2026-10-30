#include "internal.hpp"

#include <BRep_Tool.hxx>
#include <Graphic3d_AlphaMode.hxx>
#include <Image_Texture.hxx>
#include <Message.hxx>
#include <Message_Messenger.hxx>
#include <Message_Printer.hxx>
#include <Message_PrinterOStream.hxx>
#include <Message_ProgressRange.hxx>
#include <Poly_Triangulation.hxx>
#include <RWGltf_CafReader.hxx>
#include <RWMesh_CoordinateSystem.hxx>
#include <RWMesh_NodeAttributes.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Iterator.hxx>
#include <XCAFDoc_VisMaterial.hxx>

#include <algorithm>
#include <array>
#include <cstdint>
#include <memory>
#include <mutex>
#include <sstream>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

// Definition of the opaque handle declared in the public header: the tables the mtk_scene_get_* functions expose.
struct mtk_scene {
    struct Block {
        std::string name;
        int32_t first_mesh = 0;
        int32_t mesh_count = 0;
        int32_t first_reference = 0;
        int32_t reference_count = 0;
    };

    struct Mesh {
        int32_t material = -1;
        std::vector<double> vertices;
        std::vector<float> normals;
        std::vector<float> texture_coordinates;
        std::vector<int32_t> triangles;
    };

    struct Reference {
        std::string name;
        int32_t block = 0;
        std::array<double, 12> transform{};
    };

    struct Material {
        std::string name;
        std::array<float, 4> base_color{1.0f, 1.0f, 1.0f, 1.0f};
        float metallic = 0.0f;
        float roughness = 1.0f;
        int32_t base_color_texture = -1;
    };

    struct Texture {
        std::string name;
        std::string media_type;
        std::vector<uint8_t> data;
    };

    std::vector<Block> blocks;
    std::vector<Mesh> meshes;
    std::vector<Reference> references;
    std::vector<Material> materials;
    std::vector<Texture> textures;
    std::vector<std::string> warnings;
};

// The managed side declares the same layouts: these sizes must match the ones it checks in its tests.
static_assert(sizeof(void*) == 8, "Only 64-bit platforms are supported.");
static_assert(sizeof(mtk_scene_info) == 24);
static_assert(sizeof(mtk_block_info) == 24);
static_assert(sizeof(mtk_mesh_info) == 48);
static_assert(sizeof(mtk_reference_info) == 112);
static_assert(sizeof(mtk_material_info) == 40);
static_assert(sizeof(mtk_texture_info) == 32);

namespace {

using AttributeMap = NCollection_DataMap<TopoDS_Shape, RWMesh_NodeAttributes, TopTools_ShapeMapHasher>;

// Collects the messages OpenCASCADE reports while reading a file.
class MessageCollector : public Message_Printer {
public:
    mutable std::vector<std::string> warnings;
    mutable std::vector<std::string> failures;

    void send(const TCollection_AsciiString& text, const Message_Gravity gravity) const override {
        if (gravity >= Message_Fail) {
            failures.emplace_back(text.ToCString());
        } else if (gravity >= Message_Warning) {
            warnings.emplace_back(text.ToCString());
        }
    }
};

// Registers a MessageCollector with the default messenger for the lifetime of the object.
class ScopedMessageCollector {
public:
    ScopedMessageCollector() {
        // By default OpenCASCADE prints on standard output, which a command line tool reserves for its results.
        static std::once_flag silence_default_printers;
        std::call_once(silence_default_printers, [] {
            Message::DefaultMessenger()->RemovePrinters(STANDARD_TYPE(Message_PrinterOStream));
        });
        Message::DefaultMessenger()->AddPrinter(collector_);
    }

    ~ScopedMessageCollector() { Message::DefaultMessenger()->RemovePrinter(collector_); }

    ScopedMessageCollector(const ScopedMessageCollector&) = delete;
    ScopedMessageCollector& operator=(const ScopedMessageCollector&) = delete;

    const MessageCollector& messages() const { return *collector_; }

private:
    occ::handle<MessageCollector> collector_ = new MessageCollector();
};

// The glTF reader of OpenCASCADE, keeping the shapes it builds and their attributes (names, materials). Without a
// document, nothing is copied into XDE: there, nodes holding only meshes are flattened and shared meshes duplicated,
// while the shapes keep each mesh once, placed by every node that uses it.
class GltfReader : public RWGltf_CafReader {
public:
    const NCollection_Sequence<TopoDS_Shape>& roots() const { return myRootShapes; }
    const AttributeMap& attributes() const { return myAttribMap; }
};

const RWMesh_NodeAttributes* find_attributes(const AttributeMap& attributes, const TopoDS_Shape& shape) {
    return attributes.Seek(shape);
}

std::array<double, 12> to_matrix(const gp_Trsf& transform) {
    std::array<double, 12> matrix{};
    for (int row = 1; row <= 3; ++row) {
        for (int column = 1; column <= 4; ++column) {
            matrix[(row - 1) * 4 + (column - 1)] = transform.Value(row, column);
        }
    }
    return matrix;
}

// Converts the shapes read from a file into the tables of a scene, keeping shared geometry shared:
//  - every compound (a glTF node, or a mesh with several primitives) becomes a block, referenced where it is placed;
//  - a face (a primitive) used in several places becomes a block too;
//  - a face used once becomes a mesh of the block containing it.
class SceneBuilder {
public:
    SceneBuilder(const NCollection_Sequence<TopoDS_Shape>& roots, const AttributeMap& attributes)
        : roots_(roots), attributes_(attributes) {}

    std::unique_ptr<mtk_scene> build() {
        for (const TopoDS_Shape& root : roots_) {
            count_uses(root);
        }

        definitions_.emplace_back(); // block 0, the model space, is not a shape
        for (const TopoDS_Shape& root : roots_) {
            assign_blocks(root);
        }
        // Blocks are filled in index order, so that the meshes and references of each block are consecutive.
        for (size_t index = 0; index < definitions_.size(); ++index) {
            add_block(index);
        }
        if (skipped_shapes_ > 0) {
            scene_->warnings.push_back(std::to_string(skipped_shapes_) +
                                       " lines or points were skipped: only triangles are supported.");
        }
        return std::move(scene_);
    }

private:
    // Counts the places where each face is used, visiting the content of every compound once.
    void count_uses(const TopoDS_Shape& shape) {
        if (shape.ShapeType() == TopAbs_FACE) {
            ++face_uses_[shape.TShape().get()];
        } else if (shape.ShapeType() == TopAbs_COMPOUND && visited_compounds_.insert(shape.TShape().get()).second) {
            for (TopoDS_Iterator child(shape, true, false); child.More(); child.Next()) {
                count_uses(child.Value());
            }
        }
    }

    bool is_shared_face(const TopoDS_Shape& shape) const {
        return shape.ShapeType() == TopAbs_FACE && shape.Orientation() == TopAbs_FORWARD &&
               face_uses_.at(shape.TShape().get()) > 1;
    }

    bool becomes_block(const TopoDS_Shape& shape) const {
        return shape.ShapeType() == TopAbs_COMPOUND || is_shared_face(shape);
    }

    // Assigns block indices, depth first, to the shapes that become blocks.
    void assign_blocks(const TopoDS_Shape& occurrence) {
        if (!becomes_block(occurrence)) {
            return;
        }
        const auto index = static_cast<int32_t>(definitions_.size());
        if (!block_indices_.try_emplace(occurrence.TShape().get(), index).second) {
            return;
        }
        // The definition is the shape in its own coordinate system; the first occurrence provides a fallback name.
        definitions_.push_back({occurrence.Located(TopLoc_Location()).Oriented(TopAbs_FORWARD), occurrence});
        if (occurrence.ShapeType() == TopAbs_COMPOUND) {
            for (TopoDS_Iterator child(occurrence, true, false); child.More(); child.Next()) {
                assign_blocks(child.Value());
            }
        }
    }

    void add_block(size_t index) {
        mtk_scene::Block block;
        block.first_mesh = static_cast<int32_t>(scene_->meshes.size());
        block.first_reference = static_cast<int32_t>(scene_->references.size());

        if (index == 0) {
            block.name = "Model";
            for (const TopoDS_Shape& root : roots_) {
                add_occurrence(root);
            }
        } else {
            const Definition& definition = definitions_[index];
            block.name = name_of(definition.shape);
            if (block.name.empty()) {
                block.name = name_of(definition.first_occurrence);
            }
            if (block.name.empty()) {
                block.name = "Block " + std::to_string(index);
            }
            if (definition.shape.ShapeType() == TopAbs_FACE) {
                add_mesh(TopoDS::Face(definition.shape), gp_Trsf());
            } else {
                for (TopoDS_Iterator child(definition.shape, true, false); child.More(); child.Next()) {
                    add_occurrence(child.Value());
                }
            }
        }

        block.mesh_count = static_cast<int32_t>(scene_->meshes.size()) - block.first_mesh;
        block.reference_count = static_cast<int32_t>(scene_->references.size()) - block.first_reference;
        scene_->blocks.push_back(std::move(block));
    }

    // Adds a shape placed in the block being filled: a reference to its block, or its mesh.
    void add_occurrence(const TopoDS_Shape& occurrence) {
        if (becomes_block(occurrence)) {
            mtk_scene::Reference reference;
            reference.name = name_of(occurrence);
            reference.block = block_indices_.at(occurrence.TShape().get());
            reference.transform = to_matrix(occurrence.Location().Transformation());
            scene_->references.push_back(std::move(reference));
        } else if (occurrence.ShapeType() == TopAbs_FACE) {
            add_mesh(TopoDS::Face(occurrence), occurrence.Location().Transformation());
        } else {
            ++skipped_shapes_;
        }
    }

    // Adds the triangulation of a face, transformed into the coordinates of the block being filled.
    void add_mesh(const TopoDS_Face& face, const gp_Trsf& transform) {
        TopLoc_Location own_location;
        const occ::handle<Poly_Triangulation> triangulation =
            BRep_Tool::Triangulation(TopoDS::Face(face.Located(TopLoc_Location())), own_location);
        if (triangulation.IsNull() || triangulation->NbTriangles() == 0) {
            return;
        }

        const bool reversed = face.Orientation() == TopAbs_REVERSED;
        const bool mirrored = transform.VectorialPart().Determinant() < 0.0;

        mtk_scene::Mesh mesh;
        mesh.material = material_index(face);

        const int node_count = triangulation->NbNodes();
        mesh.vertices.reserve(3 * static_cast<size_t>(node_count));
        for (int node = 1; node <= node_count; ++node) {
            const gp_Pnt point = triangulation->Node(node).Transformed(transform);
            mesh.vertices.insert(mesh.vertices.end(), {point.X(), point.Y(), point.Z()});
        }
        if (triangulation->HasNormals()) {
            for (int node = 1; node <= node_count; ++node) {
                gp_Dir normal = triangulation->Normal(node).Transformed(transform);
                if (reversed) {
                    normal.Reverse();
                }
                mesh.normals.insert(mesh.normals.end(), {static_cast<float>(normal.X()),
                                                         static_cast<float>(normal.Y()),
                                                         static_cast<float>(normal.Z())});
            }
        }
        if (triangulation->HasUVNodes()) {
            for (int node = 1; node <= node_count; ++node) {
                const gp_Pnt2d uv = triangulation->UVNode(node);
                mesh.texture_coordinates.insert(mesh.texture_coordinates.end(),
                                                {static_cast<float>(uv.X()), static_cast<float>(uv.Y())});
            }
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
            mesh.triangles.insert(mesh.triangles.end(), {a - 1, b - 1, c - 1});
        }
        scene_->meshes.push_back(std::move(mesh));
    }

    std::string name_of(const TopoDS_Shape& shape) const {
        const RWMesh_NodeAttributes* attributes = find_attributes(attributes_, shape);
        return attributes != nullptr ? attributes->Name.ToCString() : std::string();
    }

    int32_t material_index(const TopoDS_Face& face) {
        // The reader attaches the material to the face of the primitive, in its own coordinate system.
        const RWMesh_NodeAttributes* attributes = find_attributes(attributes_, face.Located(TopLoc_Location()));
        if (attributes == nullptr || attributes->Style.Material().IsNull()) {
            return -1;
        }
        const occ::handle<XCAFDoc_VisMaterial>& visual = attributes->Style.Material();
        const auto index = static_cast<int32_t>(scene_->materials.size());
        const auto [found, inserted] = materials_.try_emplace(visual.get(), index);
        if (inserted) {
            scene_->materials.push_back(make_material(visual));
        }
        return found->second;
    }

    mtk_scene::Material make_material(const occ::handle<XCAFDoc_VisMaterial>& visual) {
        const XCAFDoc_VisMaterialPBR pbr =
            visual->HasPbrMaterial() ? visual->PbrMaterial() : visual->ConvertToPbrMaterial();

        mtk_scene::Material material;
        material.name = visual->RawName().IsNull() ? std::string() : visual->RawName()->ToCString();
        if (material.name.empty()) {
            material.name = "Material " + std::to_string(scene_->materials.size() + 1);
        }
        const Quantity_Color& rgb = pbr.BaseColor.GetRGB();
        const float opacity = visual->AlphaMode() == Graphic3d_AlphaMode_Opaque ? 1.0f : pbr.BaseColor.Alpha();
        material.base_color = {static_cast<float>(rgb.Red()), static_cast<float>(rgb.Green()),
                               static_cast<float>(rgb.Blue()), opacity};
        material.metallic = pbr.Metallic;
        material.roughness = pbr.Roughness;
        material.base_color_texture = texture_index(pbr.BaseColorTexture);
        return material;
    }

    int32_t texture_index(const occ::handle<Image_Texture>& texture) {
        if (texture.IsNull()) {
            return -1;
        }
        const std::string id = texture->TextureId().ToCString();
        if (const auto found = textures_.find(id); found != textures_.end()) {
            return found->second;
        }

        // The image is copied as it is stored in the file (e.g. PNG), without decoding it.
        std::ostringstream stream(std::ios::out | std::ios::binary);
        if (!texture->WriteImage(stream, texture->TextureId())) {
            scene_->warnings.push_back("A texture could not be read and was skipped.");
            textures_.emplace(id, -1);
            return -1;
        }
        const std::string bytes = stream.str();

        mtk_scene::Texture image;
        image.name = "Texture " + std::to_string(scene_->textures.size() + 1);
        image.media_type = texture->MimeType().ToCString();
        if (image.media_type.empty()) {
            image.media_type = "application/octet-stream";
        }
        image.data.assign(bytes.begin(), bytes.end());

        const auto index = static_cast<int32_t>(scene_->textures.size());
        textures_.emplace(id, index);
        scene_->textures.push_back(std::move(image));
        return index;
    }

    struct Definition {
        TopoDS_Shape shape;
        TopoDS_Shape first_occurrence;
    };

    const NCollection_Sequence<TopoDS_Shape>& roots_;
    const AttributeMap& attributes_;
    std::unique_ptr<mtk_scene> scene_ = std::make_unique<mtk_scene>();

    std::unordered_map<const TopoDS_TShape*, int> face_uses_;
    std::unordered_set<const TopoDS_TShape*> visited_compounds_;
    std::vector<Definition> definitions_;
    std::unordered_map<const TopoDS_TShape*, int32_t> block_indices_;
    std::unordered_map<const XCAFDoc_VisMaterial*, int32_t> materials_;
    std::unordered_map<std::string, int32_t> textures_;
    int skipped_shapes_ = 0;
};

std::unique_ptr<mtk_scene> read_gltf(const char* path) {
    // OpenCASCADE reports messages through a global messenger: one file at a time keeps them apart.
    static std::mutex read_mutex;
    const std::lock_guard lock(read_mutex);

    const ScopedMessageCollector collector;

    GltfReader reader;
    reader.SetFileLengthUnit(1.0); // glTF: metres, Y up
    reader.SetFileCoordinateSystem(RWMesh_CoordinateSystem_glTF);
    reader.SetSystemLengthUnit(0.001); // the toolkit: millimetres, Z up
    reader.SetSystemCoordinateSystem(RWMesh_CoordinateSystem_Zup);
    reader.SetDoublePrecision(true);
    reader.SetParallel(true);

    if (!reader.Perform(TCollection_AsciiString(path), Message_ProgressRange())) {
        std::string message = std::string("Cannot read '") + path + "'.";
        for (const std::string& failure : collector.messages().failures) {
            message += " " + failure;
        }
        throw mtk::kernel_error(message);
    }

    std::unique_ptr<mtk_scene> scene = SceneBuilder(reader.roots(), reader.attributes()).build();
    const MessageCollector& messages = collector.messages();
    scene->warnings.insert(scene->warnings.begin(), messages.warnings.begin(), messages.warnings.end());
    scene->warnings.insert(scene->warnings.begin(), messages.failures.begin(), messages.failures.end());
    return scene;
}

// Checks the arguments shared by the mtk_scene_get_* functions.
mtk_status check_item(const mtk_scene* scene, const void* output, size_t count, int32_t index) {
    if (scene == nullptr || output == nullptr) {
        return mtk::invalid_argument("The scene and the output must not be null.");
    }
    if (index < 0 || static_cast<size_t>(index) >= count) {
        return mtk::invalid_argument("Index out of range.");
    }
    return MTK_STATUS_OK;
}

template <typename T>
const T* data_or_null(const std::vector<T>& values) {
    return values.empty() ? nullptr : values.data();
}

} // namespace

mtk_status mtk_scene_read_gltf(const char* path, mtk_scene** out_scene) {
    return mtk::guarded([&] {
        if (out_scene == nullptr) {
            return mtk::invalid_argument("out_scene must not be null.");
        }
        *out_scene = nullptr;
        if (path == nullptr || *path == '\0') {
            return mtk::invalid_argument("path must not be empty.");
        }

        *out_scene = read_gltf(path).release();
        return MTK_STATUS_OK;
    });
}

void mtk_scene_release(mtk_scene* scene) {
    delete scene;
}

mtk_status mtk_scene_get_info(const mtk_scene* scene, mtk_scene_info* out_info) {
    return mtk::guarded([&] {
        if (scene == nullptr || out_info == nullptr) {
            return mtk::invalid_argument("scene and out_info must not be null.");
        }
        *out_info = {static_cast<int32_t>(scene->blocks.size()),    static_cast<int32_t>(scene->meshes.size()),
                     static_cast<int32_t>(scene->references.size()), static_cast<int32_t>(scene->materials.size()),
                     static_cast<int32_t>(scene->textures.size()),   static_cast<int32_t>(scene->warnings.size())};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_scene_get_block(const mtk_scene* scene, int32_t index, mtk_block_info* out_block) {
    return mtk::guarded([&] {
        const mtk_status status = check_item(scene, out_block, scene ? scene->blocks.size() : 0, index);
        if (status != MTK_STATUS_OK) {
            return status;
        }
        const mtk_scene::Block& block = scene->blocks[index];
        *out_block = {block.name.c_str(), block.first_mesh, block.mesh_count, block.first_reference,
                      block.reference_count};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_scene_get_mesh(const mtk_scene* scene, int32_t index, mtk_mesh_info* out_mesh) {
    return mtk::guarded([&] {
        const mtk_status status = check_item(scene, out_mesh, scene ? scene->meshes.size() : 0, index);
        if (status != MTK_STATUS_OK) {
            return status;
        }
        const mtk_scene::Mesh& mesh = scene->meshes[index];
        *out_mesh = {mesh.material,
                     static_cast<int32_t>(mesh.vertices.size() / 3),
                     static_cast<int32_t>(mesh.triangles.size() / 3),
                     data_or_null(mesh.vertices),
                     data_or_null(mesh.normals),
                     data_or_null(mesh.texture_coordinates),
                     data_or_null(mesh.triangles)};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_scene_get_reference(const mtk_scene* scene, int32_t index, mtk_reference_info* out_reference) {
    return mtk::guarded([&] {
        const mtk_status status = check_item(scene, out_reference, scene ? scene->references.size() : 0, index);
        if (status != MTK_STATUS_OK) {
            return status;
        }
        const mtk_scene::Reference& reference = scene->references[index];
        out_reference->name = reference.name.c_str();
        out_reference->block = reference.block;
        std::copy(reference.transform.begin(), reference.transform.end(), out_reference->transform);
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_scene_get_material(const mtk_scene* scene, int32_t index, mtk_material_info* out_material) {
    return mtk::guarded([&] {
        const mtk_status status = check_item(scene, out_material, scene ? scene->materials.size() : 0, index);
        if (status != MTK_STATUS_OK) {
            return status;
        }
        const mtk_scene::Material& material = scene->materials[index];
        out_material->name = material.name.c_str();
        std::copy(material.base_color.begin(), material.base_color.end(), out_material->base_color);
        out_material->metallic = material.metallic;
        out_material->roughness = material.roughness;
        out_material->base_color_texture = material.base_color_texture;
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_scene_get_texture(const mtk_scene* scene, int32_t index, mtk_texture_info* out_texture) {
    return mtk::guarded([&] {
        const mtk_status status = check_item(scene, out_texture, scene ? scene->textures.size() : 0, index);
        if (status != MTK_STATUS_OK) {
            return status;
        }
        const mtk_scene::Texture& texture = scene->textures[index];
        *out_texture = {texture.name.c_str(), texture.media_type.c_str(), data_or_null(texture.data),
                        static_cast<int64_t>(texture.data.size())};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_scene_get_warning(const mtk_scene* scene, int32_t index, const char** out_message) {
    return mtk::guarded([&] {
        const mtk_status status = check_item(scene, out_message, scene ? scene->warnings.size() : 0, index);
        if (status != MTK_STATUS_OK) {
            return status;
        }
        *out_message = scene->warnings[index].c_str();
        return MTK_STATUS_OK;
    });
}
