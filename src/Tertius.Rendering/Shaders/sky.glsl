// The light of the sky (docs/plans/environment-map.md, "The reflected stars" and "The atmosphere"): the sky without the
// stars, the stars of the sky pixels, and the stars as a reflection's lobe sees them. StarField.cs is the C# copy of the
// star code. Directions are in camera-relative world axes, and each function turns them into root axes by the conjugate of
// frame.rootRotation, so the stars are fixed to the root frame. Needs frame.glsl.
#ifndef TERTIUS_SKY_GLSL
#define TERTIUS_SKY_GLSL

#include "frame.glsl"

const float StarCells = 150.0;                       // StarField.CellsPerUnit: cells per unit of direction
const float StarCellWidth = 1.0 / StarCells;         // c, in rad
const float StarShare = 0.02;                        // one cell in 50 holds a star
// StarField.UnitRadiance: the peak radiance, in W/(m²·sr), of a star of brightness 1 (docs/plans/local-light-shadows.md,
// settled fork 1, physical stars). At 1080p and a 60° view, the brightest stars then send about 10⁻⁸ W/m².
const float StarUnitRadiance = 1.0 / 1024.0;
const float StarMeanBrightness = StarUnitRadiance * (0.05 + 3.0 / 9.0);   // E[brightness]
const vec3 StarMeanTint = vec3(0.86, 0.82, 0.81);    // E[tint]

// Hash without sine (Dave Hoskins), stable for the integer cell coordinates used below.
vec3 hash33(vec3 p)
{
    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.xxy + p.yxx) * p.zyx);
}

// A direction in world axes turned into root axes: the inverse of rootRotation, by the quaternion sandwich with the
// conjugate.
vec3 rootAxesOf(vec3 direction)
{
    vec3 u = -frame.rootRotation.xyz;
    return direction + 2.0 * cross(u, cross(u, direction) + frame.rootRotation.w * direction);
}

// p: the angle, in rad, that one pixel of the main view spans, kept above 0 so every star width is positive.
float starPixelAngle()
{
    return max(frame.params.z, 1e-5);
}

// The star of a cell of the grid: false for a cell with no star. Otherwise its direction in root axes, a hashed point of
// the cell seen from the eye, and its peak radiance in the sky, b = tint × brightness.
bool starOfCell(vec3 cell, out vec3 star, out vec3 peak)
{
    vec3 h = hash33(cell);
    star = vec3(0.0);
    peak = vec3(0.0);
    if (h.x > StarShare)
        return false;
    star = normalize(cell + hash33(cell + 17.0));
    float brightness = StarUnitRadiance * (0.05 + 3.0 * pow(h.y, 8.0));
    vec3 tint = mix(vec3(1.0, 0.82, 0.62), vec3(0.72, 0.82, 1.0), h.z);
    peak = tint * brightness;
    return true;
}

// The sky's light without the stars, in plain radiance: black until the atmosphere (docs/plans/environment-map.md,
// decision 14). The atmosphere changes this body alone.
vec3 skyRadiance(vec3 direction)
{
    return vec3(0.0);
}

// The stars of a sky pixel: the one cell that holds the direction, each star a Gaussian one pixel wide. One cell in fifty
// holds a star, which gives about 450 stars per steradian along the axes, most of them dim.
vec3 starRadiance(vec3 direction)
{
    vec3 root = rootAxesOf(direction);
    vec3 star, peak;
    if (!starOfCell(floor(root * StarCells), star, peak))
        return vec3(0.0);
    vec3 offset = root - star;
    float pixel = starPixelAngle();
    return peak * exp(-dot(offset, offset) / (pixel * pixel));
}

// w = √(f² + 4α²): the width, in rad, of a star that a lobe of width α reflects under a pixel's footprint f.
float starWidth(float footprint, float alpha)
{
    return sqrt(footprint * footprint + 4.0 * alpha * alpha);
}

// M(ω): the mean star radiance near `direction`. A sphere of radius 150 cells crosses 150² |ω|₁ cells per sr around ω,
// and each star sends E[b] π p².
vec3 starMeanRadiance(vec3 direction)
{
    vec3 root = abs(rootAxesOf(direction));
    float cellsPerSteradian = StarCells * StarCells * (root.x + root.y + root.z);
    float pixel = starPixelAngle();
    float light = StarMeanBrightness * PI * pixel * pixel;
    return cellsPerSteradian * StarShare * light * StarMeanTint;
}

// Whether the sphere of radius 150 crosses a cell, the unit cube from its integer corner: whether some direction of the
// sky reads it. The corners are integers, so no cell's span holds 0 inside it.
bool skyReadsCell(vec3 cell)
{
    vec3 farthest = max(abs(cell), abs(cell + 1.0));
    vec3 nearest = min(abs(cell), abs(cell + 1.0));
    const float radiusSquared = StarCells * StarCells;
    return dot(nearest, nearest) <= radiusSquared && dot(farthest, farthest) >= radiusSquared;
}

// K(θ): a star's Gaussian of width w at θ² = angleSquared, cut off smoothly at one cell, so that the sum over the cells
// does not depend on where the direction lies in its cell.
float starKernel(float angleSquared, float width)
{
    float window = 1.0 - angleSquared / (StarCellWidth * StarCellWidth);
    return window <= 0.0 ? 0.0 : exp(-angleSquared / (width * width)) * window * window;
}

// k(w): the share of a star's light that K keeps. Called only below the end of the fade, where a = c² / w² ≥ 0.25 and the
// closed form is finite.
float starKeptShare(float width)
{
    float a = StarCellWidth * StarCellWidth / (width * width);
    return 1.0 - 2.0 / a + 2.0 * (1.0 - exp(-a)) / (a * a);
}

// stars(d, r, f): the stars around d as a lobe of `width` w reflects them, before the open-sky share. The 27 cells around
// d hold every star within one cell of it. Single stars fade into their mean M between w = c and 2c, and the fill keeps
// the mean of the whole term at M for every width.
vec3 reflectedStars(vec3 d, float width)
{
    float fade = smoothstep(StarCellWidth, 2.0 * StarCellWidth, width);
    vec3 mean = starMeanRadiance(d);
    // Past the fade, k is never needed, so it is never computed.
    if (fade >= 1.0)
        return mean;

    vec3 root = rootAxesOf(d);
    vec3 home = floor(root * StarCells);
    float pixel = starPixelAngle();
    float peakScale = pixel * pixel / (width * width);
    vec3 stars = vec3(0.0);
    for (int z = -1; z <= 1; z++)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                vec3 cell = home + vec3(x, y, z);
                vec3 star, peak;
                // A cell that no sky direction reads holds no star of the sky either.
                if (skyReadsCell(cell) && starOfCell(cell, star, peak))
                {
                    vec3 offset = root - star;
                    stars += peak * (peakScale * starKernel(dot(offset, offset), width));
                }
            }
        }
    }

    return (1.0 - fade) * stars + (1.0 - (1.0 - fade) * starKeptShare(width)) * mean;
}

#endif
