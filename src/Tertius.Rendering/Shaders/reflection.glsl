// The trace and the cone of the screen-space reflections (docs/plans/screen-space-reflections.md, "The method").
// ScreenRay, ReflectionCone and CameraView.EdgeFade are the C# copy. Change both together.
#ifndef TERTIUS_REFLECTION_GLSL
#define TERTIUS_REFLECTION_GLSL

#include "depth_pyramid.glsl"
#include "screen.glsl"

const float ConeTangentPerAlpha = 1.7602; // ReflectionCone.TangentPerAlpha: sqrt((1 − 0.244) / 0.244)
const float EdgeFadeShare = 0.1;          // CameraView.EdgeFadeShare
const float CrossingNudge = 0.01;         // ScreenRay.CrossingNudge, in pixels
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

// The parameter where the image leaves a cell of a level, on its far edge across or down, or the segment's end. side is
// the cell's side in pixels, and farSide max(travel, 0).
float cellExit(ScreenRay ray, RayReciprocals inverse, vec2 cell, float side, vec2 farSide)
{
    vec2 edge = (cell + farSide) * side;
    float exitX = parameterAt(edge.x - ray.start.x, ray.step.x, inverse.step.x, NoEnd);
    float exitY = parameterAt(edge.y - ray.start.y, ray.step.y, inverse.step.y, NoEnd);
    return min(min(exitX, exitY), ray.end);
}

// Whether a cell at side pixels per cell lies beyond the screen, tested in pixels with no size query: such a cell holds
// no surface, as one outside its level does and as the depth image's padding does.
bool beyondTheScreen(vec2 cell, float side)
{
    return any(lessThan(cell, vec2(0.0))) || any(greaterThanEqual(cell * side, frame.viewport.xy));
}

// The bounds of a cell of a level above 0, or the empty bounds for a cell beyond the screen.
vec2 tracedCellBounds(texture2D pyramid, uint level, vec2 cell, float side)
{
    return beyondTheScreen(cell, side) ? vec2(0.0, EmptyFar) : texelFetch(pyramid, ivec2(cell), int(level) - 1).rg;
}

// The parameter where the image enters a pixel: the later of its crossings of the pixel's near edges.
float pixelEntry(ScreenRay ray, vec2 pixel, vec2 travel)
{
    vec2 edge = pixel + max(-travel, 0.0);
    float entryX = parameterAt(edge.x - ray.start.x, ray.step.x, -NoEnd);
    float entryY = parameterAt(edge.y - ray.start.y, ray.step.y, -NoEnd);
    return max(entryX, entryY);
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

// The trace's test at level 0 (ScreenRay.MeetPixel): whether the stretch of the image inside a pixel, from s to exit,
// meets the slab of thickness κ behind the pixel's surface. The ray and the slab are each read at both ends of the
// stretch, and taken as affine between them. Where the surface's part stops at a center line, the surface bends there,
// so a hit is always real, but a ray can pass the bend inside the slab and still miss: at most half a pixel's gradient
// in inverse depth off the surface, and only beside a depth edge or a curve beyond ContinuationShare ("The trace
// through the pyramid"). inW is the ray's inverse depth at s: w(s) after a crossing, the slab bound that s was solved
// for after a descent ("Precision and determinism"). On a hit, gives where the ray enters the slab, clamped into the
// pixel, and q from the pixel's edge where the image enters it.
bool meetsPixelSurface(ScreenRay ray, vec2 pixel, vec2 travel, float s, float inW, float exit, out vec2 hit,
    out float entryShare)
{
    hit = vec2(0.0);
    entryShare = 0.0;
    PixelSurface surface;
    if (beyondTheScreen(pixel, 1.0) || !pixelSurface(ivec2(pixel), surface))
        return false;
    const float thickness = 1.0 + ThicknessShare;
    float surfaceIn = surfaceW(surface, ray.start + ray.step * s);
    float surfaceOut = surfaceW(surface, ray.start + ray.step * exit);
    float outW = ray.startW + ray.stepW * exit;
    // How far the ray stands in front of the slab's front, and of its back, at both ends of the stretch.
    float frontIn = inW - surfaceIn;
    float frontOut = outW - surfaceOut;
    float backIn = inW - surfaceIn / thickness;
    float backOut = outW - surfaceOut / thickness;
    if ((frontIn > 0.0 && frontOut > 0.0) || (backIn < 0.0 && backOut < 0.0))
        return false;
    // The divisors are never 0 where the select keeps them: a stretch that starts in front of the front meets it, so
    // frontOut ≤ 0 < frontIn, and the same holds behind the back.
    float crossed = frontIn > 0.0 ? frontIn / (frontIn > 0.0 ? frontIn - frontOut : 1.0)
        : backIn < 0.0 ? backIn / (backIn < 0.0 ? backIn - backOut : 1.0) : 0.0;
    hit = clamp(ray.start + ray.step * (s + (exit - s) * crossed), pixel, pixel + 1.0);
    // From the inverse depth where the image enters the pixel, never at a solved s, which keeps a far surface's thin
    // slab exact ("Precision and determinism").
    float edge = pixelEntry(ray, pixel, travel);
    float surfaceAtEdge = surfaceW(surface, ray.start + ray.step * edge);
    entryShare = entryShareOf(ray.startW + ray.stepW * edge, surfaceAtEdge / thickness, surfaceAtEdge);
    return true;
}

// Walks the ray's image through the depth pyramid (Uludag 2014, with min-max cells and the slab of McGuire and Mara
// 2014). On a hit, gives the hit's position in pixels, its pixel and the entry share q.
bool traceRay(texture2D pyramid, ScreenRay ray, uint maxSteps, out vec2 hit, out ivec2 hitPixel, out float entryShare)
{
    uint topLevel = uint(textureQueryLevels(pyramid));
    RayReciprocals inverse = reciprocalsOf(ray);
    vec2 travel = sign(ray.step);
    vec2 farSide = max(travel, 0.0);
    float s = cellExit(ray, inverse, floor(ray.start), 1.0, farSide);
    // The inverse depth at s. After a descent it is the slab bound that s was solved for, not w(s): near the vanishing
    // point the rounding of that sum is larger than a far surface's slab.
    float inW = ray.startW + ray.stepW * s;
    uint level = 0u;
    // Whether s stands on a cell's edge, where the crossing nudge gives it to the cell ahead. A slab entry that a
    // descent solves strictly inside a cell is not on an edge, and takes no nudge.
    bool onEdge = true;
    for (uint step = 0u; step < maxSteps && s < ray.end; step++)
    {
        vec2 nudge = onEdge ? travel * CrossingNudge : vec2(0.0);
        // A product by 2^−level is the quotient by 2^level, exactly.
        float side = exactPowerOfTwo(int(level));
        vec2 cell = floor((ray.start + ray.step * s + nudge) * exactPowerOfTwo(-int(level)));
        float exit = cellExit(ray, inverse, cell, side, farSide);
        if (level == 0u && meetsPixelSurface(ray, cell, travel, s, inW, exit, hit, entryShare))
        {
            hitPixel = ivec2(cell);
            return true;
        }
        // At level 0 the pixel's surface decided: the ray crosses the pixel. Above, the cell's slab decides.
        vec2 bounds = level == 0u ? vec2(0.0, EmptyFar) : tracedCellBounds(pyramid, level, cell, side);
        float high = bounds.x;
        float low = bounds.y * (1.0 / (1.0 + ThicknessShare));
        float outW = ray.startW + ray.stepW * exit;
        bool meetsSlab = max(min(inW, outW), low) <= min(max(inW, outW), high);
        // Both outcomes as selects, so that the lanes of a wave that cross and those that descend take one path.
        // min and max, not clamp: exit can lie a rounding below s, and clamp is undefined when its minimum passes its
        // maximum.
        float entry = min(max(slabEntry(ray, inverse, s, inW, low, high), s), exit);
        // A miss crosses the cell and never goes back: an entry solved inside a cell can round past the cell's exit.
        // The next descent then finds the entry at s, keeps s on an edge, and the nudge moves the walk on.
        float crossing = max(s, exit);
        bool entryInsideCell = entry > s && entry < exit;
        onEdge = !meetsSlab || (!entryInsideCell && (onEdge || entry >= exit));
        // After a descent, the slab bound that s was solved for; min and max keep it defined where the cell is missed.
        inW = meetsSlab ? min(max(inW, low), high) : ray.startW + ray.stepW * crossing;
        s = meetsSlab ? entry : crossing;
        level = meetsSlab ? level - 1u : min(level + 1u, topLevel);
    }
    return false;
}

// The level of the radiance pyramid that the cone's radius at the hit selects (Uludag 2014). The cone's tangent is the
// larger of the lobe's spread and the chord f · texelSide to the next texel's ray, so that the read covers the gap
// between neighboring rays ("The cone"). The floor keeps log2 finite for a hit at the origin; any level below 0 reads
// level 0.
float coneLevel(float alpha, float footprint, float texelSide, float rayLength, float hitDistance, float topLevel)
{
    float spread = max(ConeTangentPerAlpha * alpha, footprint * texelSide);
    float radiusPixels = rayLength * spread / (hitDistance * frame.params.z);
    return clamp(log2(max(radiusPixels, 1e-6)), 0.0, topLevel);
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
