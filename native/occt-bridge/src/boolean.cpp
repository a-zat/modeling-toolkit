#include "internal.hpp"

#include <BRepAlgoAPI_Common.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <NCollection_List.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS_Shape.hxx>

#include <sstream>
#include <string>

namespace {

// Runs a boolean operation of OpenCASCADE: the algorithm works on lists of arguments and tools.
template <typename Operation>
mtk_status run(const TopoDS_Shape& first, const TopoDS_Shape& second, mtk_shape** out_shape) {
    Operation operation;
    operation.SetRunParallel(true);
    // The operands are left as they are, tolerances included, as other threads may be reading them.
    operation.SetNonDestructive(true);
    operation.SetArguments(NCollection_List<TopoDS_Shape>{first});
    operation.SetTools(NCollection_List<TopoDS_Shape>{second});
    operation.Build();

    if (operation.HasErrors() || !operation.IsDone()) {
        std::ostringstream report;
        operation.DumpErrors(report);
        std::string text = report.str();
        throw mtk::kernel_error("The boolean operation failed" + (text.empty() ? std::string(".") : ": " + text));
    }

    // Shapes that do not meet leave nothing in common, and a shape cut by itself nothing at all.
    const TopoDS_Shape& result = operation.Shape();
    if (result.IsNull() || !TopExp_Explorer(result, TopAbs_FACE).More()) {
        throw mtk::kernel_error("The result of the boolean operation is empty.");
    }

    *out_shape = new mtk_shape{result};
    return MTK_STATUS_OK;
}

} // namespace

mtk_status mtk_shape_boolean(const mtk_shape* first, const mtk_shape* second, int32_t operation,
                             mtk_shape** out_shape) {
    return mtk::guarded([&] {
        if (first == nullptr || second == nullptr || out_shape == nullptr) {
            return mtk::invalid_argument("first, second and out_shape must not be null.");
        }
        *out_shape = nullptr;

        switch (operation) {
        case MTK_BOOLEAN_UNION:
            return run<BRepAlgoAPI_Fuse>(first->value, second->value, out_shape);
        case MTK_BOOLEAN_DIFFERENCE:
            return run<BRepAlgoAPI_Cut>(first->value, second->value, out_shape);
        case MTK_BOOLEAN_INTERSECTION:
            return run<BRepAlgoAPI_Common>(first->value, second->value, out_shape);
        default:
            return mtk::invalid_argument("operation is not a boolean operation.");
        }
    });
}
