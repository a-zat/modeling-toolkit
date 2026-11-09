#pragma once

#include "mtk/occt_viewer.h"

#include <AIS_InteractiveObject.hxx>
#include <Quantity_Color.hxx>
#include <gp_Pnt.hxx>

#include <optional>
#include <string>
#include <vector>

namespace mtk {

// The overlay as the viewer keeps it, from checked arguments.
struct Overlay {
    struct Line {
        std::vector<gp_Pnt> points;
        Quantity_Color color;
        float width;
        float dash;
    };
    struct Marker {
        gp_Pnt point;
        Quantity_Color color;
        mtk_viewer_marker_shape shape;
    };
    struct Label {
        gp_Pnt point;
        std::string text;
        Quantity_Color color;
        Quantity_Color background;
    };

    std::vector<Line> lines;
    std::vector<Marker> markers;
    std::vector<Label> labels;

    bool empty() const { return lines.empty() && markers.empty() && labels.empty(); }
};

// The overlay of the arguments, or what is wrong with them.
std::optional<Overlay> make_overlay(const mtk_viewer_overlay& overlay, std::string& error);

// Draws an overlay, in sizes scaled by the pixels of a device-independent unit. It is never picked, and does not count
// in the bounds of the scene (it is "infinite" for FitAll).
class OverlayObject : public AIS_InteractiveObject {
    DEFINE_STANDARD_RTTI_INLINE(OverlayObject, AIS_InteractiveObject)

public:
    OverlayObject(Overlay overlay, double scale);

    bool AcceptDisplayMode(const int mode) const override { return mode == 0; }

private:
    void Compute(const occ::handle<PrsMgr_PresentationManager>& manager,
                 const occ::handle<Prs3d_Presentation>& presentation, const int mode) override;
    void ComputeSelection(const occ::handle<SelectMgr_Selection>&, const int) override {}

    Overlay overlay_;
    double scale_;
};

} // namespace mtk
