#include "mtk/occt_bridge.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <array>
#include <cstring>
#include <limits>
#include <memory>
#include <string>
#include <vector>

namespace {

struct SceneDeleter {
    void operator()(mtk_scene* scene) const noexcept { mtk_scene_release(scene); }
};

using ScenePtr = std::unique_ptr<mtk_scene, SceneDeleter>;
using Transform = std::array<double, 12>;

constexpr Transform identity = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};

std::string asset(const std::string& name) {
    return std::string(MTK_TEST_ASSETS_DIR) + "/gltf/" + name;
}

ScenePtr read(const std::string& name) {
    mtk_scene* scene = nullptr;
    EXPECT_EQ(mtk_scene_read_gltf(asset(name).c_str(), nullptr, &scene), MTK_STATUS_OK) << mtk_last_error_message();
    return ScenePtr(scene);
}

mtk_scene_info info_of(const mtk_scene* scene) {
    mtk_scene_info info{};
    EXPECT_EQ(mtk_scene_get_info(scene, &info), MTK_STATUS_OK);
    return info;
}

Transform compose(const Transform& a, const Transform& b) {
    Transform result{};
    for (int row = 0; row < 3; ++row) {
        for (int column = 0; column < 4; ++column) {
            double value = column == 3 ? a[row * 4 + 3] : 0.0;
            for (int k = 0; k < 3; ++k) {
                value += a[row * 4 + k] * b[k * 4 + column];
            }
            result[row * 4 + column] = value;
        }
    }
    return result;
}

struct Box {
    std::array<double, 3> min{std::numeric_limits<double>::infinity(), std::numeric_limits<double>::infinity(),
                              std::numeric_limits<double>::infinity()};
    std::array<double, 3> max{-std::numeric_limits<double>::infinity(), -std::numeric_limits<double>::infinity(),
                              -std::numeric_limits<double>::infinity()};
};

// Accumulates the vertices of a block, placed with `transform`, following its references.
void accumulate(const mtk_scene* scene, int32_t block_index, const Transform& transform, Box& box) {
    mtk_block_info block{};
    ASSERT_EQ(mtk_scene_get_block(scene, block_index, &block), MTK_STATUS_OK);
    for (int32_t i = 0; i < block.mesh_count; ++i) {
        mtk_mesh_info mesh{};
        ASSERT_EQ(mtk_scene_get_mesh(scene, block.first_mesh + i, &mesh), MTK_STATUS_OK);
        for (int32_t v = 0; v < mesh.vertex_count; ++v) {
            const double* p = mesh.vertices + 3 * v;
            for (int axis = 0; axis < 3; ++axis) {
                const double* m = transform.data() + axis * 4;
                const double value = m[0] * p[0] + m[1] * p[1] + m[2] * p[2] + m[3];
                box.min[axis] = std::min(box.min[axis], value);
                box.max[axis] = std::max(box.max[axis], value);
            }
        }
    }
    for (int32_t i = 0; i < block.reference_count; ++i) {
        mtk_reference_info reference{};
        ASSERT_EQ(mtk_scene_get_reference(scene, block.first_reference + i, &reference), MTK_STATUS_OK);
        Transform local{};
        std::copy(std::begin(reference.transform), std::end(reference.transform), local.begin());
        accumulate(scene, reference.block, compose(transform, local), box);
    }
}

Box world_box(const mtk_scene* scene) {
    Box box;
    accumulate(scene, 0, identity, box);
    return box;
}

void expect_box(const Box& box, std::array<double, 3> min, std::array<double, 3> max) {
    for (int axis = 0; axis < 3; ++axis) {
        EXPECT_NEAR(box.min[axis], min[axis], 1e-3) << "axis " << axis;
        EXPECT_NEAR(box.max[axis], max[axis], 1e-3) << "axis " << axis;
    }
}

// Number of references to each block.
std::vector<int> reference_counts(const mtk_scene* scene) {
    const mtk_scene_info info = info_of(scene);
    std::vector<int> counts(info.block_count, 0);
    for (int32_t i = 0; i < info.reference_count; ++i) {
        mtk_reference_info reference{};
        EXPECT_EQ(mtk_scene_get_reference(scene, i, &reference), MTK_STATUS_OK);
        ++counts.at(reference.block);
    }
    return counts;
}

// Expected values computed independently from the glTF files: vertices through the node transformations, converted
// from metres and Y up to millimetres and Z up.

TEST(Scene, BoxIsReadInMillimetres) {
    ScenePtr scene = read("Box.glb");
    ASSERT_NE(scene, nullptr);

    const mtk_scene_info info = info_of(scene.get());
    EXPECT_EQ(info.mesh_count, 1);
    EXPECT_EQ(info.material_count, 1);
    EXPECT_EQ(info.warning_count, 0);

    mtk_mesh_info mesh{};
    ASSERT_EQ(mtk_scene_get_mesh(scene.get(), 0, &mesh), MTK_STATUS_OK);
    EXPECT_EQ(mesh.vertex_count, 24);
    EXPECT_EQ(mesh.triangle_count, 12);
    EXPECT_NE(mesh.normals, nullptr);
    EXPECT_EQ(mesh.material, 0);

    expect_box(world_box(scene.get()), {-500, -500, -500}, {500, 500, 500});
}

TEST(Scene, AxesAreConvertedToZUp) {
    ScenePtr scene = read("SimpleMeshes.gltf");
    ASSERT_NE(scene, nullptr);

    expect_box(world_box(scene.get()), {0, 0, 0}, {2000, 0, 1000});
}

TEST(Scene, SharedMeshesBecomeOneBlockReferencedTwice) {
    for (const char* name : {"SimpleMeshes.gltf", "CesiumMilkTruck.glb"}) {
        ScenePtr scene = read(name);
        ASSERT_NE(scene, nullptr);

        const std::vector<int> counts = reference_counts(scene.get());
        EXPECT_EQ(std::count(counts.begin(), counts.end(), 2), 1) << name;
    }
}

TEST(Scene, HierarchyMaterialsAndTexturesAreRead) {
    ScenePtr scene = read("CesiumMilkTruck.glb");
    ASSERT_NE(scene, nullptr);

    const mtk_scene_info info = info_of(scene.get());
    EXPECT_EQ(info.mesh_count, 4);
    EXPECT_EQ(info.material_count, 4);
    // The file has two glTF textures showing the same JPEG image: the image is kept once.
    EXPECT_EQ(info.texture_count, 1);

    expect_box(world_box(scene.get()), {-1395.999944, -2437.999853, 1.451834}, {1395.999944, 2430.910063, 2584.369807});
}

TEST(Scene, TexturesKeepTheirEncodedImage) {
    ScenePtr scene = read("BoxTextured.glb");
    ASSERT_NE(scene, nullptr);
    ASSERT_EQ(info_of(scene.get()).texture_count, 1);

    mtk_texture_info texture{};
    ASSERT_EQ(mtk_scene_get_texture(scene.get(), 0, &texture), MTK_STATUS_OK);
    EXPECT_STREQ(texture.media_type, "image/png");
    ASSERT_GT(texture.size, 8);
    EXPECT_EQ(std::memcmp(texture.data, "\x89PNG", 4), 0);

    mtk_material_info material{};
    ASSERT_EQ(mtk_scene_get_material(scene.get(), 0, &material), MTK_STATUS_OK);
    EXPECT_EQ(material.base_color_texture, 0);

    mtk_mesh_info mesh{};
    ASSERT_EQ(mtk_scene_get_mesh(scene.get(), 0, &mesh), MTK_STATUS_OK);
    EXPECT_NE(mesh.texture_coordinates, nullptr);
}

TEST(Scene, MeshesWithoutMaterialHaveNone) {
    ScenePtr scene = read("SimpleMeshes.gltf");
    ASSERT_NE(scene, nullptr);
    EXPECT_EQ(info_of(scene.get()).material_count, 0);

    mtk_mesh_info mesh{};
    ASSERT_EQ(mtk_scene_get_mesh(scene.get(), 0, &mesh), MTK_STATUS_OK);
    EXPECT_EQ(mesh.material, -1);
}

TEST(Scene, MissingFileIsAKernelError) {
    mtk_scene* scene = nullptr;
    EXPECT_EQ(mtk_scene_read_gltf(asset("Missing.glb").c_str(), nullptr, &scene), MTK_STATUS_KERNEL_ERROR);
    EXPECT_EQ(scene, nullptr);
    EXPECT_NE(std::string(mtk_last_error_message()).find("Missing.glb"), std::string::npos);
}

TEST(Scene, InvalidArgumentsAreRejected) {
    mtk_scene* out = nullptr;
    EXPECT_EQ(mtk_scene_read_gltf(nullptr, nullptr, &out), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_scene_read_gltf(asset("Box.glb").c_str(), nullptr, nullptr), MTK_STATUS_INVALID_ARGUMENT);

    ScenePtr scene = read("Box.glb");
    ASSERT_NE(scene, nullptr);
    mtk_block_info block{};
    EXPECT_EQ(mtk_scene_get_block(scene.get(), -1, &block), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_scene_get_block(scene.get(), info_of(scene.get()).block_count, &block), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_scene_get_block(nullptr, 0, &block), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(mtk_scene_get_block(scene.get(), 0, nullptr), MTK_STATUS_INVALID_ARGUMENT);
    mtk_scene_release(nullptr);
}

TEST(Scene, ClearsTheOutputsOfACallThatFails) {
    ScenePtr scene = read("Box.glb");
    ASSERT_NE(scene, nullptr);
    mtk_block_info block{};
    ASSERT_EQ(mtk_scene_get_block(scene.get(), 0, &block), MTK_STATUS_OK);
    ASSERT_NE(block.name, nullptr);

    EXPECT_EQ(mtk_scene_get_block(scene.get(), -1, &block), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(block.name, nullptr);
    EXPECT_EQ(block.mesh_count, 0);

    mtk_scene_info info = info_of(scene.get());
    EXPECT_EQ(mtk_scene_get_info(nullptr, &info), MTK_STATUS_INVALID_ARGUMENT);
    EXPECT_EQ(info.block_count, 0);
}

} // namespace

TEST(Scene, ReportsTheProgressOfReadingAndStopsWhenAsked) {
    std::vector<double> reports;
    const mtk_progress progress{[](double done, void* user_data) -> int32_t {
                                    static_cast<std::vector<double>*>(user_data)->push_back(done);
                                    return 0;
                                },
                                &reports};
    mtk_scene* scene = nullptr;
    ASSERT_EQ(mtk_scene_read_gltf(asset("CesiumMilkTruck.glb").c_str(), &progress, &scene), MTK_STATUS_OK)
        << mtk_last_error_message();
    ScenePtr owned(scene);
    ASSERT_GE(reports.size(), 2u);
    EXPECT_EQ(reports.front(), 0.0);
    EXPECT_EQ(reports.back(), 1.0);
    EXPECT_TRUE(std::is_sorted(reports.begin(), reports.end()));

    const mtk_progress stop{[](double, void*) -> int32_t { return 1; }, nullptr};
    scene = nullptr;
    EXPECT_EQ(mtk_scene_read_gltf(asset("CesiumMilkTruck.glb").c_str(), &stop, &scene), MTK_STATUS_CANCELLED);
    EXPECT_EQ(scene, nullptr);
}
