// The depth pyramid (docs/plans/screen-space-reflections.md, "The depth pyramid"). The bounds of a cell are the largest
// and the smallest inverse view depth of the surfaces of its pixels, in 1/m: (w_near, w_far), or the empty bounds
// (0, EmptyFar) for a cell with no surface. Level 0 is the distance target with the geometric normals, read through
// pixelSurface; mip m of the pyramid image holds level m + 1. DepthPyramid, PixelSurface and CellBounds are the C# copy.
// Change both together.
#ifndef TERTIUS_DEPTH_PYRAMID_GLSL
#define TERTIUS_DEPTH_PYRAMID_GLSL

#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "screen.glsl"

// κ: a surface at view depth z fills the depths from z to z (1 + κ) behind it, along its view ray. The reflections'
// trace and the bounce light's near steps read it, so both see one model of what is solid (CellBounds.ThicknessShare).
const float ThicknessShare = 0.1;
// The share by which the center of a pixel's edge neighbor may stand off the pixel's plane, in inverse depth, and still
// continue it (CellBounds.ContinuationShare). Far below κ: at κ, the plane of a curved mesh's pixel reaches edges where
// the neighbor's surface stands off it by several percent, and rays slip through the step between the two surfaces,
// which shows as dotted lines on the reflections of curved meshes.
const float ContinuationShare = 0.01;
// The share of a pixel's distance below which its plane counts as edge-on: the plane holds the pixel's view ray to within
// a millionth of a radian. Such a pixel keeps its center's inverse depth over its square, because no neighbor could
// continue the plane, and its gradient would grow past any float (PixelSurface.EdgeOnShare).
const float EdgeOnShare = 1e-6;
// Far above any surface's inverse depth, which is at most 1 / 0.05 m. Not infinity: fast math may assume none.
const float EmptyFar = 1e30;

// The surface of a pixel of level 0 that sees a surface (docs/plans/screen-space-reflections.md, "The pixel's surface"):
// its plane, through its center point along its geometric normal, as an inverse depth that is affine in the screen
// position, over the part of its square from low to high. Toward each edge neighbor the part reaches the pixel's edge
// where the neighbor continues the plane, and stops at the pixel's center line otherwise. Past the part, the surface
// keeps the plane's value at the part's edge. The pyramid's bounds and the trace's level-0 test both read it.
struct PixelSurface
{
    vec2 center;     // the pixel's center, in pixels
    float centerW;   // the inverse depth at the center, in 1/m, from the distance target
    vec2 gradient;   // the plane's change of inverse depth per pixel, across and down, in 1/m
    vec2 low;        // the part's corner toward the screen's top left, in pixels
    vec2 high;       // its opposite corner
};

// Whether an edge neighbor continues the surface's plane: it lies beyond the image, where no depth edge lies, or it sees
// a surface whose center's inverse depth lies within ContinuationShare of the plane's there, in either direction. The
// test keeps every texelFetch inside its image.
bool continuesPlane(PixelSurface surface, ivec2 neighbor)
{
    if (any(lessThan(neighbor, ivec2(0))) || any(greaterThanEqual(neighbor, textureSize(gbufferDistance, 0))))
        return true;
    float distanceToEye = texelFetch(gbufferDistance, neighbor, 0).r;
    if (distanceToEye <= 0.0)
        return false;
    vec2 neighborCenter = vec2(neighbor) + 0.5;
    float w = length(viewRayThrough(neighborCenter)) / distanceToEye;
    float planeW = surface.centerW + dot(surface.gradient, neighborCenter - surface.center);
    const float tolerance = 1.0 + ContinuationShare;
    return w <= planeW * tolerance && w * tolerance >= planeW;
}

// The surface of a pixel inside the image, or false for a pixel that sees the sky.
bool pixelSurface(ivec2 pixel, out PixelSurface surface)
{
    surface = PixelSurface(vec2(0.0), 0.0, vec2(0.0), vec2(0.0), vec2(0.0));
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (distanceToEye <= 0.0)
        return false;
    surface.center = vec2(pixel) + 0.5;
    vec3 centerRay = viewRayThrough(surface.center);
    surface.centerW = length(centerRay) / distanceToEye;
    surface.low = surface.center;
    surface.high = surface.center;
    // The plane holds the points p with n · p = planeDistance. On the view ray r through a screen position, whose
    // forward part is 1, it lies at the inverse depth n · r / planeDistance, which is affine in the position.
    vec3 n = octDecode(texelFetch(gbufferNormal, pixel, 0).ba);
    float planeDistance = dot(n, normalize(centerRay) * distanceToEye);
    if (abs(planeDistance) <= EdgeOnShare * distanceToEye)
        return true;
    surface.gradient = vec2(dot(n, frame.cameraRight.xyz) * 2.0 * frame.viewport.z,
        -dot(n, frame.cameraUp.xyz) * 2.0 * frame.viewport.w) / planeDistance;
    surface.low = vec2(continuesPlane(surface, pixel + ivec2(-1, 0)) ? float(pixel.x) : surface.center.x,
        continuesPlane(surface, pixel + ivec2(0, -1)) ? float(pixel.y) : surface.center.y);
    surface.high = vec2(continuesPlane(surface, pixel + ivec2(1, 0)) ? float(pixel.x + 1) : surface.center.x,
        continuesPlane(surface, pixel + ivec2(0, 1)) ? float(pixel.y + 1) : surface.center.y);
    return true;
}

// The surface's inverse depth on the view ray through a screen position, in 1/m.
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
