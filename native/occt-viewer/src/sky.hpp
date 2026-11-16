#pragma once

#include <Graphic3d_CubeMap.hxx>
#include <gp_Dir.hxx>

namespace mtk {

// A sky as a cubemap, painted on the processor: blue above, lighter at the horizon, red about the sun when it is low,
// dark at night, with the disk of the sun and clouds, more of them the higher the cloudiness (0 to 1). The sun is a
// direction of the model, Z up. Drawn by the background cubemap of OpenCASCADE, which also lights physically shaded
// models with it.
occ::handle<Graphic3d_CubeMap> make_sky(const gp_Dir& sun, float cloudiness, int size = 512);

} // namespace mtk
