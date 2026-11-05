// The depth pyramid (docs/plans/screen-space-reflections.md, "The depth pyramid", and docs/plans/temporal-renderer.md,
// section 6). The bounds of a cell are the largest and the smallest inverse view depth of the surfaces of its pixels, in
// 1/m: (w_near, w_far), or the empty bounds (0, EmptyFar) for a cell with no surface. The finest level holds each pixel's
// center w (depth_pyramid_finest.comp). The bounds of level 1 come from the pixels' surfaces, read through pixelSurface;
// mip m of the pyramid image holds level m + 1. DepthPyramid, PixelSurface and CellBounds are the C# copy.
// Change both together.
#ifndef TERTIUS_DEPTH_PYRAMID_GLSL
#define TERTIUS_DEPTH_PYRAMID_GLSL

#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "screen.glsl"
#include "thickness.glsl"

// The share by which the center of a depth sample's edge neighbor may stand off the sample's plane, in inverse depth, and
// the sample's center off the neighbor's plane, where the neighbor continues the plane (CellBounds.ContinuationShare). Far
// below κ: at κ, the plane of a curved mesh's pixel reaches edges where the neighbor's surface stands off it by several
// percent, and rays slip through the step between the two surfaces, which shows as dotted lines on the reflections of
// curved meshes.
const float ContinuationShare = 0.01;
// The share of a sample's distance below which its plane counts as edge-on: the plane holds the sample's view ray to
// within a millionth of a radian. Such a sample keeps its center's inverse depth over its square, and no neighbor
// continues it, because its gradient would grow past any float (PixelPlane.EdgeOnShare).
const float EdgeOnShare = 1e-6;
// Far above any surface's inverse depth, which is at most 1 / 0.05 m. Not infinity: fast math may assume none.
const float EmptyFar = 1e30;

// The plane of a depth sample that sees a surface: through its center point along its geometric normal, as an inverse
// depth that is affine in the place on the screen, in units of the sample's grid. A sample is a pixel, in pixels. An edge-on
// plane, which holds the sample's view ray to within EdgeOnShare, keeps its center's inverse depth, with no gradient.
struct PixelPlane
{
    vec2 center;     // the sample's center, in units of its grid
    float centerW;   // the inverse depth at the center, in 1/m
    vec2 gradient;   // the plane's change of inverse depth per unit of the grid, across and down, in 1/m
    bool edgeOn;     // whether the plane is edge-on
};

// The plane of a depth sample in its own grid (PixelPlane.OfSample). The plane holds the points p with n · p =
// planeDistance, n the sample's unit geometric normal and P its center point, at pointDistance |P| from the eye. On the
// view ray r through a place, whose forward part is 1, it lies at the inverse depth n · r / planeDistance, which is
// affine in the place: normalAlongGrid is n · Δr, the normal's dot product with the ray's change per unit of the grid.
PixelPlane samplePlane(vec2 center, float centerW, float planeDistance, float pointDistance, vec2 normalAlongGrid)
{
    PixelPlane plane = PixelPlane(center, centerW, vec2(0.0), abs(planeDistance) <= EdgeOnShare * pointDistance);
    if (!plane.edgeOn)
        plane.gradient = normalAlongGrid / planeDistance;
    return plane;
}

// The inverse view depth, in 1/m, of the surface point at distanceToEye m along the view ray through a position on the
// screen: the ray's length over the distance, because the ray's forward part is 1. The one rule of a depth sample's w:
// a pixel's plane and the finest level (depth_pyramid_finest.comp) both call it.
float inverseDepthAlong(vec2 position, float distanceToEye)
{
    return length(viewRayThrough(position)) / distanceToEye;
}

// The plane of a pixel inside the image, or false for a pixel that sees the sky.
bool pixelPlane(ivec2 pixel, out PixelPlane plane)
{
    plane = PixelPlane(vec2(0.0), 0.0, vec2(0.0), false);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (distanceToEye <= 0.0)
        return false;
    vec2 center = vec2(pixel) + 0.5;
    vec3 centerRay = viewRayThrough(center);
    vec3 n = octDecode(texelFetch(gbufferNormal, pixel, 0).ba);
    // The view ray changes by cameraRight · 2 / W across a pixel and by −cameraUp · 2 / H down one.
    plane = samplePlane(center, inverseDepthAlong(center, distanceToEye), dot(n, normalize(centerRay) * distanceToEye),
        distanceToEye, vec2(dot(n, frame.cameraRight.xyz) * 2.0 * frame.viewport.z,
            -dot(n, frame.cameraUp.xyz) * 2.0 * frame.viewport.w));
    return true;
}

// Whether the inverse depth w at a screen position lies within ContinuationShare of the plane's there, in either
// direction.
bool planeHolds(PixelPlane plane, vec2 position, float w)
{
    float planeW = plane.centerW + dot(plane.gradient, position - plane.center);
    const float tolerance = 1.0 + ContinuationShare;
    return w <= planeW * tolerance && w * tolerance >= planeW;
}

// Whether an edge neighbor continues a depth sample's plane. A neighbor beyond the image continues it, as no depth edge
// lies there, and one that sees the sky does not. One that sees a surface, whose plane is other, continues it where
// neither plane is edge-on and each plane holds the other sample's center. Both directions, because a steep plane at a
// depth edge can pass the far surface's center one sample away by chance, and the far surface's plane never passes back
// through the near center. The one rule of where a depth sample's plane holds (PixelPlane.ContinuesInto), which the
// pixels' surfaces call. other is read only for a
// neighbor inside the image that sees a surface, so a caller reads no sample beyond the image.
bool neighborContinues(PixelPlane plane, bool insideImage, bool seesSurface, PixelPlane other)
{
    if (!insideImage)
        return true;
    return seesSurface && !plane.edgeOn && !other.edgeOn
        && planeHolds(plane, other.center, other.centerW) && planeHolds(other, plane.center, plane.centerW);
}

// Whether an edge neighbor continues a pixel's plane (neighborContinues). Every texelFetch stays inside its image.
bool continuesPlane(PixelPlane plane, ivec2 neighbor)
{
    bool inside = all(greaterThanEqual(neighbor, ivec2(0))) && all(lessThan(neighbor, textureSize(gbufferDistance, 0)));
    PixelPlane other = PixelPlane(vec2(0.0), 0.0, vec2(0.0), false);
    bool seesSurface = inside && pixelPlane(neighbor, other);
    return neighborContinues(plane, inside, seesSurface, other);
}

// The surface of a depth sample that sees a surface (docs/plans/screen-space-reflections.md, "The pixel's surface"):
// its plane over the part of its square, one unit of its grid wide, from low to high. Toward each edge neighbor the part
// reaches the sample's edge where the neighbor continues the plane, and stops at the sample's center line otherwise.
// Past the part, the surface keeps the plane's value at the part's edge. The bounds of level 1 read the surface of each
// pixel.
struct PixelSurface
{
    vec2 center;     // the sample's center, in units of its grid
    float centerW;   // the inverse depth at the center, in 1/m
    vec2 gradient;   // the plane's change of inverse depth per unit of the grid, across and down, in 1/m
    vec2 low;        // the part's corner toward the screen's top left, in units of the grid
    vec2 high;       // its opposite corner
};

// The surface of a sample: its plane over the part of its square that reaches the edge toward each neighbor whose flag
// in `reaches` (left, up, right, down) is true, and stops at the center line toward the others (PixelSurface.Over).
PixelSurface sampleSurface(PixelPlane plane, bvec4 reaches)
{
    return PixelSurface(plane.center, plane.centerW, plane.gradient, plane.center - 0.5 * vec2(reaches.xy),
        plane.center + 0.5 * vec2(reaches.zw));
}

// The surface of a pixel inside the image, or false for a pixel that sees the sky.
bool pixelSurface(ivec2 pixel, out PixelSurface surface)
{
    surface = PixelSurface(vec2(0.0), 0.0, vec2(0.0), vec2(0.0), vec2(0.0));
    PixelPlane plane;
    if (!pixelPlane(pixel, plane))
        return false;
    surface = sampleSurface(plane, bvec4(continuesPlane(plane, pixel + ivec2(-1, 0)),
        continuesPlane(plane, pixel + ivec2(0, -1)), continuesPlane(plane, pixel + ivec2(1, 0)),
        continuesPlane(plane, pixel + ivec2(0, 1))));
    return true;
}

// The surface's inverse depth on the view ray through a place, in units of the sample's grid, in 1/m.
float surfaceW(PixelSurface surface, vec2 position)
{
    return surface.centerW + dot(surface.gradient, clamp(position, surface.low, surface.high) - surface.center);
}

// The bounds of a pixel inside the image: the nearest and the farthest inverse depth of its surface over its square, or
// the empty bounds for sky. The surface is affine over its part and constant past it, so the part's corners hold both.
vec2 pixelBounds(ivec2 pixel)
{
    PixelSurface surface;
    if (!pixelSurface(pixel, surface))
        return vec2(0.0, EmptyFar);
    vec2 toLow = surface.gradient * (surface.low - surface.center);
    vec2 toHigh = surface.gradient * (surface.high - surface.center);
    // Summed in the order of the C# copy, so that both round alike.
    return vec2(surface.centerW + max(toLow.x, toHigh.x) + max(toLow.y, toHigh.y),
        surface.centerW + min(toLow.x, toHigh.x) + min(toLow.y, toHigh.y));
}

vec2 mergedBounds(vec2 a, vec2 b)
{
    return vec2(max(a.x, b.x), min(a.y, b.y));
}

// A cell outside its level holds no surface: no surface lies beyond the screen's edges. The test also keeps every
// texelFetch inside its image, because the Vulkan spec leaves a fetch out of range undefined without the robustness
// features.
vec2 cellBounds(texture2D pyramid, uint level, ivec2 cell)
{
    ivec2 size = level == 0u ? textureSize(gbufferDistance, 0) : textureSize(pyramid, int(level) - 1);
    if (any(lessThan(cell, ivec2(0))) || any(greaterThanEqual(cell, size)))
        return vec2(0.0, EmptyFar);
    return level == 0u ? pixelBounds(cell) : texelFetch(pyramid, cell, int(level) - 1).rg;
}

#endif
