// The depth pyramid (docs/plans/screen-space-reflections.md, "The depth pyramid", docs/plans/temporal-renderer.md,
// section 6, and docs/plans/back-face-depth-layer.md). Every level holds slabs: the interval of inverse view depth w, in
// 1/m, that the walk treats as solid, as (front w, back w). The finest level holds each pixel's slab (pixelSlab,
// depth_pyramid_finest.comp). A cell of level 1 merges its pixels' slab bounds (pixelSlabBounds) from their surfaces
// (pixelSurface). The merge takes the largest front w and the smallest back w. A cell with no surface holds the empty
// bounds (0, EmptyBack). Mip m of the pyramid image holds level m + 1. A pixel's slab comes from its front surface and
// its back share (BackFacePass). The build is the one place of the slab rule (slabBack): only the finest shader and the
// shader of level 1 call it. DepthPyramid, PixelSurface and CellBounds are the C# copy. Change both together.
#ifndef TERTIUS_DEPTH_PYRAMID_GLSL
#define TERTIUS_DEPTH_PYRAMID_GLSL

#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "screen.glsl"

// How far the center of a depth sample's edge neighbor may stand off the sample's plane where the neighbor continues
// the plane, as a share of inverse depth (CellBounds.ContinuationShare). The sample's center may stand off the
// neighbor's plane by as much. At a share of 0.1, the plane of a curved mesh's pixel reaches edges where the neighbor's
// surface stands off it by several percent. Rays then slip through the step between the two surfaces, which shows as
// dotted lines on the reflections of curved meshes.
const float ContinuationShare = 0.01;
// The share of a sample's distance below which its plane counts as edge-on (PixelPlane.EdgeOnShare). Such a plane holds
// the sample's view ray to within a millionth of a radian. The sample keeps its center's inverse depth over its square,
// and no neighbor continues it, because its gradient would grow past any float.
const float EdgeOnShare = 1e-6;
// The back w of a cell with no surface: far above any surface's inverse depth, which is at most 1 / 0.05 m. Not
// infinity: fast math may assume none.
const float EmptyBack = 1e30;

// The plane of a depth sample that sees a surface: through its center point along its geometric normal. It is an
// inverse depth that is affine in the place on the screen, in units of the sample's grid. A sample is a pixel, in
// pixels. An edge-on plane, which holds the sample's view ray to within EdgeOnShare, keeps its center's inverse depth,
// with no gradient.
struct PixelPlane
{
    vec2 center;     // the sample's center, in units of its grid
    float centerW;   // the inverse depth at the center, in 1/m
    vec2 gradient;   // the plane's change of inverse depth per unit of the grid, across and down, in 1/m
    bool edgeOn;     // whether the plane is edge-on
};

// The plane of a depth sample in its own grid (PixelPlane.OfSample). The plane holds the points p with n · p =
// planeDistance, with n the sample's unit geometric normal. P is the sample's center point, at pointDistance |P| from
// the eye. On the view ray r through a place, whose forward part is 1, the plane lies at the inverse depth
// n · r / planeDistance, which is affine in the place. normalAlongGrid is n · Δr, the normal's dot product with the
// ray's change per unit of the grid.
PixelPlane samplePlane(vec2 center, float centerW, float planeDistance, float pointDistance, vec2 normalAlongGrid)
{
    PixelPlane plane = PixelPlane(center, centerW, vec2(0.0), abs(planeDistance) <= EdgeOnShare * pointDistance);
    if (!plane.edgeOn)
        plane.gradient = normalAlongGrid / planeDistance;
    return plane;
}

// The inverse view depth, in 1/m, of the surface point at distanceToEye m along the view ray through a position on the
// screen. It is the ray's length over the distance, because the ray's forward part is 1. The one rule of a depth
// sample's w: a pixel's plane and the finest level (depth_pyramid_finest.comp) both call it.
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
// neither plane is edge-on and each plane holds the other sample's center. The test runs in both directions, because a
// steep plane at a depth edge can pass the far surface's center one sample away by chance. The far surface's plane
// never passes back through the near center. This is the one rule of where a depth sample's plane holds
// (PixelPlane.ContinuesInto), which the pixels' surfaces call. other is read only for a neighbor inside the image that
// sees a surface, so a caller reads no sample beyond the image.
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
    bool inside = all(greaterThanEqual(neighbor, ivec2(0))) && all(lessThan(neighbor, ivec2(frame.viewport.xy)));
    PixelPlane other = PixelPlane(vec2(0.0), 0.0, vec2(0.0), false);
    bool seesSurface = inside && pixelPlane(neighbor, other);
    return neighborContinues(plane, inside, seesSurface, other);
}

// The surface of a depth sample that sees a surface (docs/plans/screen-space-reflections.md, "The pixel's surface"). It
// is the sample's plane over the part of its square from low to high, in a square one unit of its grid wide. Toward
// each edge neighbor the part reaches the sample's edge where the neighbor continues the plane. Otherwise it stops at
// the sample's center line.
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

// The surface of a sample (PixelSurface.Over): its plane over a part of its square. The flags of `reaches` are left,
// up, right and down. The part reaches the edge toward each neighbor whose flag is true. It stops at the center line
// toward the others.
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

// The bounds of a pixel inside the image: the nearest and the farthest inverse depth of its surface over its square. A
// pixel that sees the sky has the empty bounds. The surface is affine over its part and constant past it, so the part's
// corners hold both.
vec2 pixelBounds(ivec2 pixel)
{
    PixelSurface surface;
    if (!pixelSurface(pixel, surface))
        return vec2(0.0, EmptyBack);
    vec2 toLow = surface.gradient * (surface.low - surface.center);
    vec2 toHigh = surface.gradient * (surface.high - surface.center);
    // Summed in the order of the C# copy, so that both round alike.
    return vec2(surface.centerW + max(toLow.x, toHigh.x) + max(toLow.y, toHigh.y),
        surface.centerW + min(toLow.x, toHigh.x) + min(toLow.y, toHigh.y));
}

// The w of the back of what is solid behind a surface at inverse depth `w` whose back share is `share`
// (docs/plans/back-face-depth-layer.md, decision 3, and CellBounds.SlabBack). It is `w * share` for a share between 0
// and 1. A sheet, whose share is 0 or 1, gets `w` itself.
float slabBack(float w, float share)
{
    return share > 0.0 && share < 1.0 ? w * share : w;
}

// The slab of a pixel at the finest level whose back share is `share` (DepthPyramid.PixelSlab): x the center w, y the
// back w, or (0, 0) for sky. The back w is slabBack of the center w, or the plane's far w where that lies farther. So a
// sheet's slab reaches the far end of its plane over its square. A pixel beyond the viewport holds sky, so that the
// inactive pixels of a smaller draw never enter the pyramid.
vec2 pixelSlab(ivec2 pixel, float share)
{
    float distanceToEye = all(lessThan(pixel, ivec2(frame.viewport.xy))) ? texelFetch(gbufferDistance, pixel, 0).r : 0.0;
    if (distanceToEye <= 0.0)
        return vec2(0.0);
    float w = inverseDepthAlong(vec2(pixel) + 0.5, distanceToEye);
    return vec2(w, min(slabBack(w, share), pixelBounds(pixel).y));
}

// The bounds of a pixel inside the viewport for level 1, whose back share is `share` (PixelSurface.SlabBounds): x its
// plane's near w, y the back w of its slab from its plane's far w, or the empty bounds for sky. The plane's near w is at
// least the center w and its far w at most. So these bounds hold the pixel's slab, because a share is at most 1.
vec2 pixelSlabBounds(ivec2 pixel, float share)
{
    vec2 bounds = pixelBounds(pixel);
    // The sky keeps the empty bounds, which leave every merge unchanged.
    if (bounds.x > 0.0)
        bounds.y = slabBack(bounds.y, share);
    return bounds;
}

vec2 mergedBounds(vec2 a, vec2 b)
{
    return vec2(max(a.x, b.x), min(a.y, b.y));
}

#endif
