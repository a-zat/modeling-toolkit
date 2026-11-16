#include "sky.hpp"

#include <Graphic3d_CubeMapSeparate.hxx>
#include <Image_PixMap.hxx>
#include <NCollection_Array1.hxx>

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace {

struct Rgb {
    double r, g, b;
};

Rgb mix(const Rgb& a, const Rgb& b, double t) {
    return {a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t};
}

double smoothstep(double edge0, double edge1, double x) {
    const double t = std::clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

// Value noise on a lattice, smooth between its points, from 0 to 1.
double lattice(int x, int y) {
    uint32_t h = static_cast<uint32_t>(x) * 374761393u + static_cast<uint32_t>(y) * 668265263u;
    h = (h ^ (h >> 13)) * 1274126177u;
    return static_cast<double>((h ^ (h >> 16)) & 0xffffu) / 65535.0;
}

double noise(double x, double y) {
    const double fx = std::floor(x), fy = std::floor(y);
    const int ix = static_cast<int>(fx), iy = static_cast<int>(fy);
    const double tx = smoothstep(0.0, 1.0, x - fx), ty = smoothstep(0.0, 1.0, y - fy);
    const double top = lattice(ix, iy) + (lattice(ix + 1, iy) - lattice(ix, iy)) * tx;
    const double bottom = lattice(ix, iy + 1) + (lattice(ix + 1, iy + 1) - lattice(ix, iy + 1)) * tx;
    return top + (bottom - top) * ty;
}

double clouds(double x, double y) {
    double sum = 0.0, amplitude = 0.5;
    for (int octave = 0; octave < 5; ++octave) {
        sum += amplitude * noise(x, y);
        x = x * 2.03 + 17.1;
        y = y * 2.03 - 9.7;
        amplitude *= 0.5;
    }
    return sum; // 0 to about 1
}

// The color of the sky seen along a direction of the model.
Rgb sky_color(double x, double y, double z, const gp_Dir& sun, double cloudiness) {
    const double elevation = sun.Z();                            // sine of the height of the sun
    const double day = smoothstep(-0.12, 0.15, elevation);       // 0 at night, 1 by day
    const double dusk = smoothstep(0.35, 0.0, std::abs(elevation)) * smoothstep(-0.2, 0.0, elevation);

    const Rgb zenith = mix({0.07, 0.09, 0.20}, {0.20, 0.42, 0.78}, day);
    const Rgb horizon = mix(mix({0.14, 0.17, 0.30}, {0.70, 0.80, 0.92}, day), {0.95, 0.55, 0.30}, dusk * 0.8);
    const double up = std::max(z, 0.0);
    Rgb color = mix(horizon, zenith, std::pow(up, 0.45));

    // Below the horizon: a haze darker downwards, mostly hidden by the ground.
    if (z < 0.0) {
        const Rgb below = mix({0.08, 0.09, 0.12}, {0.42, 0.44, 0.45}, day);
        color = mix(horizon, below, smoothstep(0.0, 0.25, -z));
    }

    // The glow about the sun, wider and redder when it is low, and its disk.
    const double toward = x * sun.X() + y * sun.Y() + z * sun.Z();
    if (toward > 0.0) {
        const Rgb glow_color = mix({1.0, 0.95, 0.85}, {1.0, 0.5, 0.2}, dusk);
        const double glow = std::pow(toward, 8.0) * (0.25 + 0.5 * dusk) * smoothstep(-0.1, 0.05, elevation);
        color = mix(color, glow_color, std::min(glow, 1.0));
        const double disk = smoothstep(0.9993, 0.9997, toward) * smoothstep(-0.02, 0.01, elevation);
        color = mix(color, {1.0, 0.98, 0.9}, disk);
    }

    // Clouds on a plane above: far ones near the horizon, thinned out there.
    if (z > 0.01 && cloudiness > 0.0) {
        const double scale = 1.0 / (z + 0.08);
        const double density = clouds(x * scale * 0.45, y * scale * 0.45);
        const double threshold = 0.72 - 0.35 * cloudiness; // no clouds at 0, overcast at 1
        const double cover = smoothstep(threshold, threshold + 0.15, density) * smoothstep(0.01, 0.15, z);
        if (cover > 0.0) {
            const Rgb lit = mix({0.08, 0.08, 0.11}, mix({0.97, 0.97, 0.98}, {1.0, 0.68, 0.45}, dusk), day);
            const double shade = 1.0 - 0.3 * smoothstep(threshold + 0.1, threshold + 0.4, density); // thick clouds are darker
            const Rgb cloud{lit.r * shade, lit.g * shade, lit.b * shade};
            color = mix(color, cloud, cover * 0.9);
        }
    }
    return color;
}

uint8_t byte(double value) {
    return static_cast<uint8_t>(std::lround(std::clamp(value, 0.0, 1.0) * 255.0));
}

} // namespace

occ::handle<Graphic3d_CubeMap> mtk::make_sky(const gp_Dir& sun, float cloudiness, int size) {
    NCollection_Array1<occ::handle<Image_PixMap>> faces(0, 5);
    for (int face = 0; face < 6; ++face) {
        occ::handle<Image_PixMap> image = new Image_PixMap();
        image->InitTrash(Image_Format_RGB, static_cast<size_t>(size), static_cast<size_t>(size));
        image->SetTopDown(true);
        for (int row = 0; row < size; ++row) {
            const double t = 2.0 * (row + 0.5) / size - 1.0;
            for (int column = 0; column < size; ++column) {
                const double s = 2.0 * (column + 0.5) / size - 1.0;
                // The direction of the texel in the cubemap, faces in the order +X, -X, +Y, -Y, +Z, -Z of OpenGL.
                double cx = 0, cy = 0, cz = 0;
                switch (face) {
                case 0: cx = 1; cy = -t; cz = -s; break;
                case 1: cx = -1; cy = -t; cz = s; break;
                case 2: cx = s; cy = 1; cz = t; break;
                case 3: cx = s; cy = -1; cz = -t; break;
                case 4: cx = s; cy = -t; cz = 1; break;
                default: cx = -s; cy = -t; cz = -1; break;
                }
                // OpenCASCADE samples the direction (y, z, x) of the model, so the cubemap is Y up.
                const double length = std::sqrt(cx * cx + cy * cy + cz * cz);
                const Rgb color = sky_color(cz / length, cx / length, cy / length, sun, cloudiness);
                Image_ColorRGB& texel = image->ChangeValue<Image_ColorRGB>(static_cast<size_t>(row),
                                                                           static_cast<size_t>(column));
                texel.r() = byte(color.r);
                texel.g() = byte(color.g);
                texel.b() = byte(color.b);
            }
        }
        faces.SetValue(face, image);
    }
    return new Graphic3d_CubeMapSeparate(faces);
}
