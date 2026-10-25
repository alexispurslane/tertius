// The trace and the cone of the screen-space reflections (docs/plans/screen-space-reflections.md, "The method").
// ScreenRay, ReflectionCone and CameraView.EdgeFade are the C# copy. Change both together.
#ifndef TERTIUS_REFLECTION_GLSL
#define TERTIUS_REFLECTION_GLSL

#include "depth_pyramid.glsl"
#include "screen.glsl"

const float ThicknessShare = 0.1;         // ScreenRay.ThicknessShare
const float ConeTangentPerAlpha = 1.7602; // ReflectionCone.TangentPerAlpha: sqrt((1 − 0.244) / 0.244)
const float EdgeFadeShare = 0.1;          // CameraView.EdgeFadeShare
const float CrossingNudge = 0.01;         // ScreenRay.CrossingNudge, in pixels
const float OriginOffsetPixels = 2.0;     // ScreenRay.OriginOffsetPixels
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

ScreenRay screenRayOf(vec3 origin, vec3 direction)
{
    vec3 forward = frame.cameraForward.xyz;
    float originDepth = dot(origin, forward);
    float towardDepth = dot(direction, forward);
    // A second point in front of the eye, so that both projections are finite: one view depth along the ray, or
    // halfway to the eye's plane.
    float along = towardDepth >= 0.0 ? originDepth : 0.5 * originDepth / -towardDepth;
    vec3 second = origin + direction * along;
    ScreenRay ray;
    ray.start = pixelOf(origin);
    ray.step = pixelOf(second) - ray.start;
    ray.startW = 1.0 / originDepth;
    ray.stepW = 1.0 / dot(second, forward) - ray.startW;
    float end = min(segmentEnd(ray.start.x, ray.step.x, frame.viewport.x),
        segmentEnd(ray.start.y, ray.step.y, frame.viewport.y));
    // The near plane for a ray toward the eye, the vanishing point for a ray away from it.
    ray.end = min(end, segmentEnd(ray.startW, ray.stepW, 1.0 / NearPlane));
    return ray;
}

// The parameter where the image leaves a cell of a level, on its far edge across or down, or the segment's end.
float cellExit(ScreenRay ray, uint level, ivec2 cell, vec2 travel)
{
    vec2 edge = (vec2(cell) + max(travel, 0.0)) * float(1u << level);
    float exitX = parameterAt(edge.x - ray.start.x, ray.step.x, NoEnd);
    float exitY = parameterAt(edge.y - ray.start.y, ray.step.y, NoEnd);
    return min(min(exitX, exitY), ray.end);
}

// The parameter where the image enters a pixel: the later of its crossings of the pixel's near edges.
float pixelEntry(ScreenRay ray, ivec2 pixel, vec2 travel)
{
    vec2 edge = vec2(pixel) + max(-travel, 0.0);
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
// is already inside.
float slabEntry(ScreenRay ray, float s, float inW, float low, float high)
{
    if (inW > high)
        return parameterAt(high - ray.startW, ray.stepW, s);
    if (inW < low)
        return parameterAt(low - ray.startW, ray.stepW, s);
    return s;
}

// Walks the ray's image through the depth pyramid (Uludag 2014, with min-max cells and the slab of McGuire and Mara
// 2014). On a hit, gives the hit's position in pixels, its pixel and the entry share q.
bool traceRay(texture2D pyramid, ScreenRay ray, uint maxSteps, out vec2 hit, out ivec2 hitPixel, out float entryShare)
{
    uint topLevel = uint(textureQueryLevels(pyramid));
    vec2 travel = sign(ray.step);
    float s = cellExit(ray, 0u, ivec2(floor(ray.start)), travel);
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
        ivec2 cell = ivec2(floor((ray.start + ray.step * s + nudge) / float(1u << level)));
        float exit = cellExit(ray, level, cell, travel);
        vec2 bounds = cellBounds(pyramid, level, cell);
        float high = bounds.x;
        float low = bounds.y / (1.0 + ThicknessShare);
        float outW = ray.startW + ray.stepW * exit;
        if (max(min(inW, outW), low) > min(max(inW, outW), high))
        {
            // Never back: an entry solved inside a cell can round past the cell's exit. The next descent then finds
            // the entry at s, keeps s on an edge, and the nudge moves the walk on.
            s = max(s, exit);
            inW = ray.startW + ray.stepW * s;
            onEdge = true;
            level = min(level + 1u, topLevel);
            continue;
        }
        // min and max, not clamp: exit can lie a rounding below s, and clamp is undefined when its minimum passes its
        // maximum.
        float entry = min(max(slabEntry(ray, s, inW, low, high), s), exit);
        if (level == 0u)
        {
            hit = clamp(ray.start + ray.step * entry, vec2(cell), vec2(cell) + 1.0);
            hitPixel = cell;
            // From the inverse depth where the image enters the pixel, never at a solved s, which keeps a far
            // surface's thin slab exact ("Precision and determinism").
            entryShare = entryShareOf(ray.startW + ray.stepW * pixelEntry(ray, cell, travel), low, high);
            return true;
        }
        bool entryInsideCell = entry > s && entry < exit;
        onEdge = !entryInsideCell && (onEdge || entry >= exit);
        s = entry;
        // The overlap above keeps low at most high, so this clamp is defined.
        inW = clamp(inW, low, high);
        level--;
    }
    return false;
}

// The level of the radiance pyramid that the cone's radius at the hit selects (Uludag 2014). The floor keeps log2
// finite for a hit at the origin; any level below 0 reads level 0.
float coneLevel(float alpha, float rayLength, float hitDistance, float topLevel)
{
    float radiusPixels = rayLength * ConeTangentPerAlpha * alpha / (hitDistance * frame.params.z);
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
