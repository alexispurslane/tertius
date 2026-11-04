// The image of a ray on the screen and the geometry of the depth pyramid's cells that a walk crosses
// (docs/plans/temporal-renderer.md, section 6, "The traversal"). ScreenRay and CameraView.EdgeFade are the C# copy.
// Change both together.
#ifndef TERTIUS_SCREEN_RAY_GLSL
#define TERTIUS_SCREEN_RAY_GLSL

#include "depth_pyramid.glsl"
#include "screen.glsl"

const float EdgeFadeShare = 0.1;          // CameraView.EdgeFadeShare
const float OriginOffsetPixels = 2.0;     // ScreenRay.OriginOffsetPixels
const float MostOriginOffsetShare = 0.5;  // ScreenRay.MostOriginOffsetShare
const float SurfaceFadeCosine = 0.05;     // ScreenRay.SurfaceFadeCosine
const float NearPlane = 0.05;             // DepthRanges.NearPlane
// The parameter of a segment that no limit ends. Not infinity: fast math may assume none.
const float NoEnd = 1e30;

// The ray's image: S(s) = start + s · step in pixels, w(s) = startW + s · stepW in 1/m, for s from 0 to end.
struct ScreenRay
{
    vec2 start;
    vec2 step;
    float startW;
    float stepW;
    float end;
};

// The parameter at which a coordinate that moves by step per unit has moved by offset, or otherwise when it stands
// still. The divisor is never 0: the Vulkan spec leaves a division by 0 undefined, even in a lane that a select drops.
float parameterAt(float offset, float step, float otherwise)
{
    return step != 0.0 ? offset / (step != 0.0 ? step : 1.0) : otherwise;
}

// Where a coordinate that moves by step per unit reaches 0 or limit, or NoEnd when it stands still.
float segmentEnd(float position, float step, float limit)
{
    return parameterAt(step > 0.0 ? limit - position : -position, step, NoEnd);
}

// How far the origin lies off a surface at viewDepth (ScreenRay.OriginOffset): two pixel footprints, and at most half
// the view depth, so that the origin of a surface that faces the eye stays at least half as deep as the surface. In a
// window a few pixels tall, or at a field of view near a half turn, two footprints pass the view depth.
float originOffset(float viewDepth)
{
    return min(OriginOffsetPixels * frame.params.z, MostOriginOffsetShare) * viewDepth;
}

ScreenRay screenRayOf(vec3 origin, vec3 direction, float viewDepth)
{
    vec3 forward = frame.cameraForward.xyz;
    float originDepth = dot(origin, forward);
    float towardDepth = dot(direction, forward);
    // A second point in front of the eye and within one view depth of the origin, so that both projections are
    // finite and its depth does not cancel: one view depth along the ray, or halfway to the eye's plane for a ray
    // that comes toward it faster than half its length. Either way the second point lies at least half as deep.
    float along = towardDepth >= -0.5 ? originDepth : 0.5 * originDepth / -towardDepth;
    vec3 second = origin + direction * along;
    float secondDepth = dot(second, forward);
    ScreenRay ray;
    // The origin lies at least half as deep as its surface (originOffset), and the second point half as deep again. A
    // computed depth far below those comes from the rounding of a view ray longer than about 10^7, whose error passes
    // its forward part of 1, and can be 0 or below. Such a ray gets an empty segment before any reciprocal
    // (ScreenRay's NoSegment).
    if (originDepth < 0.25 * viewDepth || secondDepth < 0.125 * viewDepth)
    {
        ray.start = vec2(0.0);
        ray.step = vec2(0.0);
        ray.startW = 0.0;
        ray.stepW = 0.0;
        ray.end = -1.0;
        return ray;
    }
    ray.start = pixelOf(origin);
    ray.step = pixelOf(second) - ray.start;
    ray.startW = 1.0 / originDepth;
    ray.stepW = 1.0 / secondDepth - ray.startW;
    float end = min(segmentEnd(ray.start.x, ray.step.x, frame.viewport.x),
        segmentEnd(ray.start.y, ray.step.y, frame.viewport.y));
    // The near plane for a ray toward the eye, the vanishing point for a ray away from it.
    ray.end = min(end, segmentEnd(ray.startW, ray.stepW, 1.0 / NearPlane));
    return ray;
}

// The reciprocals of a ray's steps, which every examined cell divides by, computed once per ray. Each axis that stands
// still takes 1, as parameterAt's divisor does, and its parameters are never read.
struct RayReciprocals
{
    vec2 step;
    float stepW;
};

RayReciprocals reciprocalsOf(ScreenRay ray)
{
    RayReciprocals inverse;
    inverse.step = 1.0 / mix(ray.step, vec2(1.0), equal(ray.step, vec2(0.0)));
    inverse.stepW = 1.0 / (ray.stepW != 0.0 ? ray.stepW : 1.0);
    return inverse;
}

// parameterAt with the step's reciprocal.
float parameterAt(float offset, float step, float inverseStep, float otherwise)
{
    return step != 0.0 ? offset * inverseStep : otherwise;
}

// 2^power exactly, for a power from −126 to 127: the side of a cell of level power in pixels, or its reciprocal.
float exactPowerOfTwo(int power)
{
    return uintBitsToFloat(uint(127 + power) << 23);
}

// The largest float below a positive x (C#'s MathF.BitDecrement). Every float that lies below x lies at it or below.
float floatBelow(float x)
{
    return uintBitsToFloat(floatBitsToUint(x) - 1u);
}

// The parameters where the image reaches the line x = edge.x and the line y = edge.y, or still on an axis where the
// image stands still (ScreenRay.EdgeParameters). The walk and the level-0 test take every pixel's edges from here.
vec2 edgeParameters(ScreenRay ray, RayReciprocals inverse, vec2 edge, float still)
{
    vec2 offset = edge - ray.start;
    return vec2(parameterAt(offset.x, ray.step.x, inverse.step.x, still),
        parameterAt(offset.y, ray.step.y, inverse.step.y, still));
}

// The parameter where the image leaves a cell of a level, on its far edge across or down, or the segment's end. side is
// the cell's side in pixels, and farSide max(travel, 0). crossed is 1 on each axis whose far edge gives the earlier of
// the two parameters, 0 on the other, both at a corner.
float cellExit(ScreenRay ray, RayReciprocals inverse, vec2 cell, float side, vec2 farSide, out vec2 crossed)
{
    vec2 exits = edgeParameters(ray, inverse, (cell + farSide) * side, NoEnd);
    float exit = min(exits.x, exits.y);
    crossed = vec2(lessThanEqual(exits, vec2(exit)));
    return min(exit, ray.end);
}

// Whether a cell at side pixels per cell lies beyond the screen, tested in pixels with no size query: such a cell holds
// no surface, as one outside its level does and as the depth image's padding does.
bool beyondTheScreen(vec2 cell, float side)
{
    return any(lessThan(cell, vec2(0.0))) || any(greaterThanEqual(cell * side, frame.viewport.xy));
}

// q: 0 when the ray stands in front of the slab [low, high] where it enters the pixel, 1 behind it, and between inside.
float entryShareOf(float edgeW, float low, float high)
{
    if (edgeW > high)
        return 0.0;
    if (edgeW < low)
        return 1.0;
    return (high - edgeW) / (high - low);
}

// The parameter where the ray enters a slab that it meets: its front from in front, its back from behind, or s when it
// is already inside. Selects, not branches, so that the lanes of a wave stay on one path.
float slabEntry(ScreenRay ray, RayReciprocals inverse, float s, float inW, float low, float high)
{
    bool inside = inW <= high && inW >= low;
    // Capped at the nearest surface's inverse depth, which no slab that the ray meets passes, so that the product stays
    // finite also for a missed cell's empty bounds, whose entry the trace never takes.
    float towardW = min(inW > high ? high : low, 1.0 / NearPlane);
    float solved = parameterAt(towardW - ray.startW, ray.stepW, inverse.stepW, s);
    return inside ? s : solved;
}

// 0 on the screen's edges, 1 a tenth of the shorter side inside them (Uludag 2014).
float edgeFade(vec2 pixel)
{
    vec2 size = frame.viewport.xy;
    vec2 inside = clamp(min(pixel, size - pixel) / (EdgeFadeShare * min(size.x, size.y)), 0.0, 1.0);
    vec2 smoothed = inside * inside * (3.0 - 2.0 * inside);
    return smoothed.x * smoothed.y;
}

#endif
