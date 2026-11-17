#include "overlay.hpp"

#include "scene.hpp"

#include <Graphic3d_ArrayOfPoints.hxx>
#include <Graphic3d_ArrayOfPolylines.hxx>
#include <Graphic3d_AspectLine3d.hxx>
#include <Graphic3d_AspectMarker3d.hxx>
#include <Graphic3d_AspectText3d.hxx>
#include <Graphic3d_Group.hxx>
#include <Graphic3d_MarkerImage.hxx>
#include <NCollection_HArray1.hxx>
#include <Font_NameOfFont.hxx>
#include <Prs3d_Text.hxx>
#include <Prs3d_TextAspect.hxx>
#include <TCollection_ExtendedString.hxx>

#include <algorithm>
#include <cmath>

namespace {

// In device-independent units.
constexpr double marker_scale = 3.0;
constexpr double label_height = 12.0;

bool finite(const double* values, int count) {
    return std::all_of(values, values + count, [](double value) { return std::isfinite(value); });
}

std::optional<Quantity_Color> color_of(const float* values) {
    for (int i = 0; i < 3; ++i) {
        if (!std::isfinite(values[i]) || values[i] < 0.0f || values[i] > 1.0f) {
            return std::nullopt;
        }
    }
    return Quantity_Color(values[0], values[1], values[2], Quantity_TOC_sRGB);
}

Aspect_TypeOfMarker marker_type(mtk_viewer_marker_shape shape) {
    switch (shape) {
    case MTK_VIEWER_MARKER_CIRCLE:
        return Aspect_TOM_RING1;
    case MTK_VIEWER_MARKER_CROSS:
        return Aspect_TOM_X;
    case MTK_VIEWER_MARKER_PLUS:
        return Aspect_TOM_PLUS;
    default:
        return Aspect_TOM_BALL;
    }
}

// A square, or a square on its corner, of a size in pixels, as the bitmap of a marker: rows from the top, a bit per pixel.
occ::handle<Graphic3d_AspectMarker3d> bitmap_marker(bool diamond, int size, const Quantity_Color& color) {
    const int row = (size + 7) / 8;
    occ::handle<NCollection_HArray1<uint8_t>> bits = new NCollection_HArray1<uint8_t>(0, row * size - 1, 0);
    const double half = (size - 1) / 2.0;
    for (int y = 0; y < size; ++y) {
        for (int x = 0; x < size; ++x) {
            if (!diamond || std::abs(x - half) + std::abs(y - half) <= half + 0.5) {
                bits->ChangeValue(y * row + x / 8) |= static_cast<uint8_t>(0x80 >> (x % 8));
            }
        }
    }
    return new Graphic3d_AspectMarker3d(color, size, size, bits);
}

} // namespace

std::optional<mtk::Overlay> mtk::make_overlay(const mtk_viewer_overlay& overlay, std::string& error) {
    if (overlay.line_count < 0 || overlay.marker_count < 0 || overlay.label_count < 0) {
        error = "The counts of the overlay must not be negative.";
        return std::nullopt;
    }
    if ((overlay.lines == nullptr && overlay.line_count > 0) ||
        (overlay.markers == nullptr && overlay.marker_count > 0) ||
        (overlay.labels == nullptr && overlay.label_count > 0)) {
        error = "An array of the overlay is null, but its count is not 0.";
        return std::nullopt;
    }

    Overlay result;
    for (int32_t i = 0; i < overlay.line_count; ++i) {
        const mtk_viewer_overlay_line& line = overlay.lines[i];
        const std::optional<Quantity_Color> color = color_of(line.color);
        if (line.points == nullptr || line.point_count < 2 || !finite(line.points, 3 * line.point_count) || !color ||
            !std::isfinite(line.width) || line.width <= 0.0f || !std::isfinite(line.dash) || line.dash < 0.0f) {
            error = "A line of the overlay needs 2 finite points or more, a colour from 0 to 1, a positive width and a "
                    "dash length that is not negative.";
            return std::nullopt;
        }
        Overlay::Line kept{{}, *color, line.width, line.dash};
        for (int32_t p = 0; p < line.point_count; ++p) {
            kept.points.emplace_back(line.points[3 * p], line.points[3 * p + 1], line.points[3 * p + 2]);
        }
        result.lines.push_back(std::move(kept));
    }
    for (int32_t i = 0; i < overlay.marker_count; ++i) {
        const mtk_viewer_overlay_marker& marker = overlay.markers[i];
        const std::optional<Quantity_Color> color = color_of(marker.color);
        if (!finite(marker.point, 3) || !color || marker.shape < MTK_VIEWER_MARKER_DOT ||
            marker.shape > MTK_VIEWER_MARKER_DIAMOND) {
            error = "A marker of the overlay needs a finite point, a colour from 0 to 1 and a shape.";
            return std::nullopt;
        }
        result.markers.push_back({gp_Pnt(marker.point[0], marker.point[1], marker.point[2]), *color,
                                  static_cast<mtk_viewer_marker_shape>(marker.shape)});
    }
    for (int32_t i = 0; i < overlay.label_count; ++i) {
        const mtk_viewer_overlay_label& label = overlay.labels[i];
        const std::optional<Quantity_Color> color = color_of(label.color), background = color_of(label.background);
        if (!finite(label.point, 3) || label.text == nullptr || label.text[0] == '\0' || !color || !background) {
            error = "A label of the overlay needs a finite point, a text and colours from 0 to 1.";
            return std::nullopt;
        }
        result.labels.push_back(
            {gp_Pnt(label.point[0], label.point[1], label.point[2]), label.text, *color, *background});
    }
    return result;
}

mtk::OverlayObject::OverlayObject(Overlay overlay, double scale) : overlay_(std::move(overlay)), scale_(scale) {
    SetInfiniteState(true);
}

void mtk::OverlayObject::Compute(const occ::handle<PrsMgr_PresentationManager>&,
                                 const occ::handle<Prs3d_Presentation>& presentation, const int mode) {
    if (!AcceptDisplayMode(mode)) {
        return;
    }
    const auto width = [&](double units) { return static_cast<float>(units * scale_); };

    for (const Overlay::Line& line : overlay_.lines) {
        occ::handle<Graphic3d_ArrayOfPolylines> points =
            new Graphic3d_ArrayOfPolylines(static_cast<int>(line.points.size()));
        for (const gp_Pnt& point : line.points) {
            points->AddVertex(point);
        }
        occ::handle<Graphic3d_AspectLine3d> aspect = new Graphic3d_AspectLine3d(line.color, Aspect_TOL_SOLID, 1.0);
        aspect->SetShaderProgram(outline_program(width(line.width), true, width(line.dash)));
        const occ::handle<Graphic3d_Group> group = presentation->NewGroup();
        group->SetGroupPrimitivesAspect(aspect);
        group->AddPrimitiveArray(points);
    }

    for (const Overlay::Marker& marker : overlay_.markers) {
        occ::handle<Graphic3d_ArrayOfPoints> point = new Graphic3d_ArrayOfPoints(1);
        point->AddVertex(marker.point);
        if (marker.shape == MTK_VIEWER_MARKER_SQUARE || marker.shape == MTK_VIEWER_MARKER_DIAMOND) {
            // Edged in white, to stand out on any surface: a larger white one under it.
            const bool diamond = marker.shape == MTK_VIEWER_MARKER_DIAMOND;
            const int size = static_cast<int>(std::lround((diamond ? 11.0 : 9.0) * scale_)) | 1;
            const int edge = std::max(1, static_cast<int>(std::lround(1.5 * scale_))) * 2;
            for (const auto& [pixels, color] : {std::pair{size + edge, Quantity_Color(Quantity_NOC_WHITE)},
                                                std::pair{size, marker.color}}) {
                const occ::handle<Graphic3d_Group> group = presentation->NewGroup();
                group->SetGroupPrimitivesAspect(bitmap_marker(diamond, pixels, color));
                group->AddPrimitiveArray(point);
            }
            continue;
        }
        const occ::handle<Graphic3d_Group> group = presentation->NewGroup();
        group->SetGroupPrimitivesAspect(
            new Graphic3d_AspectMarker3d(marker_type(marker.shape), marker.color, marker_scale * scale_));
        group->AddPrimitiveArray(point);
    }

    for (const Overlay::Label& label : overlay_.labels) {
        occ::handle<Prs3d_TextAspect> aspect = new Prs3d_TextAspect();
        aspect->SetColor(label.color);
        aspect->SetHeight(label_height * scale_);
        aspect->SetFont(Font_NOF_SANS_SERIF);
        aspect->Aspect()->SetDisplayType(Aspect_TODT_SUBTITLE);
        aspect->Aspect()->SetColorSubTitle(label.background);
        aspect->SetHorizontalJustification(Graphic3d_HTA_LEFT);
        aspect->SetVerticalJustification(Graphic3d_VTA_BOTTOM);
        Prs3d_Text::Draw(presentation->NewGroup(), aspect, TCollection_ExtendedString(label.text.c_str(), true),
                         label.point);
    }
}
