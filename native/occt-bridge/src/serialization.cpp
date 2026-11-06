#include "internal.hpp"

#include <BRep_Builder.hxx>
#include <BRepTools.hxx>
#include <TopTools_FormatVersion.hxx>

#include <cstdint>
#include <limits>
#include <sstream>
#include <string>

// Bytes owned by the bridge: the data of a serialized shape.
struct mtk_bytes {
    std::string data;
};

mtk_status mtk_shape_write(const mtk_shape* shape, mtk_bytes** out_bytes) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_bytes);
        if (shape == nullptr || out_bytes == nullptr) {
            return mtk::invalid_argument("shape and out_bytes must not be null.");
        }

        // The geometry only: a mesh is made again when needed, from the geometry.
        std::ostringstream stream;
        BRepTools::Write(shape->value, stream, false, false, TopTools_FormatVersion_CURRENT);
        *out_bytes = new mtk_bytes{stream.str()};
        return MTK_STATUS_OK;
    });
}

mtk_status mtk_bytes_get(const mtk_bytes* bytes, const uint8_t** out_data, int64_t* out_size) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_data, out_size);
        if (bytes == nullptr || out_data == nullptr || out_size == nullptr) {
            return mtk::invalid_argument("bytes, out_data and out_size must not be null.");
        }
        *out_data = reinterpret_cast<const uint8_t*>(bytes->data.data());
        *out_size = static_cast<int64_t>(bytes->data.size());
        return MTK_STATUS_OK;
    });
}

void mtk_bytes_release(mtk_bytes* bytes) {
    delete bytes;
}

mtk_status mtk_shape_read(const uint8_t* data, int64_t size, mtk_shape** out_shape) {
    return mtk::guarded([&] {
        mtk::clear_outputs(out_shape);
        if (out_shape == nullptr) {
            return mtk::invalid_argument("out_shape must not be null.");
        }
        if (data == nullptr || size <= 0 || static_cast<uint64_t>(size) > std::numeric_limits<size_t>::max()) {
            return mtk::invalid_argument("data must hold the bytes of a shape.");
        }

        std::istringstream stream(std::string(reinterpret_cast<const char*>(data), static_cast<size_t>(size)));
        const BRep_Builder builder;
        TopoDS_Shape shape;
        BRepTools::Read(shape, stream, builder);
        if (shape.IsNull()) {
            return mtk::invalid_argument("The data is not a shape in the BRep format.");
        }

        *out_shape = new mtk_shape{shape};
        return MTK_STATUS_OK;
    });
}
