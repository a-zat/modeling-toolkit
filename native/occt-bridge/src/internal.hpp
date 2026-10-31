#pragma once

#include "mtk/errors.hpp"
#include "mtk/occt_bridge.h"

#include <TopoDS_Shape.hxx>

// Definition of the opaque handle declared in the public header.
struct mtk_shape {
    TopoDS_Shape value;
};
