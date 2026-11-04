// The trace and the cone of the screen-space reflections (docs/plans/screen-space-reflections.md, "The method"), over
// the ray's image of screen_ray.glsl. ScreenRay.Trace and ReflectionCone are the C# copy. Change both together.
#ifndef TERTIUS_REFLECTION_GLSL
#define TERTIUS_REFLECTION_GLSL

#include "screen_ray.glsl"

const float ConeTangentPerAlpha = 1.7602; // ReflectionCone.TangentPerAlpha: sqrt((1 − 0.244) / 0.244)

// The pixel that the image enters at s, in a cell of side pixels (ScreenRay.PixelAt): on each axis, the pixel whose
// near edge the image reaches at s or before and whose far edge it reaches after s, by the parameters that the walk's
// crossings compare. So a position that rounds across a pixel's corner does not move the walk off the march's pixels.
// The position's rounding is far below a pixel, so its floor lies at most one pixel off on each axis. Clamped into the
// cell, which holds that pixel whenever s lies in the cell. side ≥ 1, so the clamp's minimum never passes its maximum.
vec2 pixelAt(ScreenRay ray, RayReciprocals inverse, vec2 cell, float side, vec2 travel, float s)
{
    vec2 pixel = floor(ray.start + ray.step * s);
    vec2 entry = edgeParameters(ray, inverse, pixel + max(-travel, 0.0), -NoEnd);
    vec2 exit = edgeParameters(ray, inverse, pixel + max(travel, 0.0), NoEnd);
    // One pixel on where the image reaches the far edge at s or before, one back where it reaches the near edge after.
    pixel += travel * (vec2(lessThanEqual(exit, vec2(s))) - vec2(greaterThan(entry, vec2(s))));
    vec2 first = cell * side;
    return clamp(pixel, first, first + (side - 1.0));
}

// The bounds of a cell of a level above 0, or the empty bounds for a cell beyond the screen.
vec2 tracedCellBounds(texture2D pyramid, uint level, vec2 cell, float side)
{
    return beyondTheScreen(cell, side) ? vec2(0.0, EmptyFar) : texelFetch(pyramid, ivec2(cell), int(level) - 1).rg;
}

// The parameter where the image enters a pixel: the later of its crossings of the pixel's near edges.
float pixelEntry(ScreenRay ray, RayReciprocals inverse, vec2 pixel, vec2 travel)
{
    vec2 entry = edgeParameters(ray, inverse, pixel + max(-travel, 0.0), -NoEnd);
    return max(entry.x, entry.y);
}

// The trace's test at level 0 (ScreenRay.MeetPixel): whether the stretch of the image inside a pixel, from where it
// enters the pixel to where it leaves it or the segment ends, meets the slab of thickness κ behind the pixel's surface.
// The ray and the slab are each read at both ends of the stretch, and taken as affine between them. The pixel alone
// gives the stretch, never a slab entry that a descent solved inside the pixel, so the answer does not depend on how
// the walk reached the pixel. Where the surface's part stops at a center line, the surface bends there, so a hit is
// always real, but a ray can pass the bend inside the slab and still miss: at most half a pixel's gradient in inverse
// depth off the surface, and only beside a depth edge or a curve beyond ContinuationShare ("The trace through the
// pyramid"). On a hit, gives where the ray enters the slab, clamped into the pixel, and q from the pixel's edge where
// the image enters it.
bool meetsPixelSurface(
    ScreenRay ray, RayReciprocals inverse, vec2 pixel, vec2 travel, out vec2 hit, out float entryShare)
{
    hit = vec2(0.0);
    entryShare = 0.0;
    PixelSurface surface;
    if (beyondTheScreen(pixel, 1.0) || !pixelSurface(ivec2(pixel), surface))
        return false;
    const float thickness = 1.0 + ThicknessShare;
    float edge = pixelEntry(ray, inverse, pixel, travel);
    vec2 leftThrough;
    float exit = cellExit(ray, inverse, pixel, 1.0, max(travel, 0.0), leftThrough);
    // The ray's inverse depth where the image enters the pixel, never at a solved s, which keeps a far surface's thin
    // slab exact ("Precision and determinism").
    float inW = ray.startW + ray.stepW * edge;
    float outW = ray.startW + ray.stepW * exit;
    float surfaceIn = surfaceW(surface, ray.start + ray.step * edge);
    float surfaceOut = surfaceW(surface, ray.start + ray.step * exit);
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
    hit = clamp(ray.start + ray.step * (edge + (exit - edge) * crossed), pixel, pixel + 1.0);
    entryShare = entryShareOf(inW, surfaceIn / thickness, surfaceIn);
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
    // The pixel that the walk stands in: a pixel of the march whose stretch holds s, the one that the image enters at
    // s after a crossing and the one that it leaves at s after a descent. The cell of each level is the one that holds
    // it, so the walk tests no pixel that the march does not visit, and skips only pixels before a slab entry.
    vec2 pixel = floor(ray.start);
    vec2 crossed;
    float s = cellExit(ray, inverse, pixel, 1.0, farSide, crossed);
    pixel += travel * crossed;
    // The inverse depth at s. After a descent it is the slab bound that s was solved for, not w(s): near the vanishing
    // point the rounding of that sum is larger than a far surface's slab.
    float inW = ray.startW + ray.stepW * s;
    uint level = 0u;
    for (uint step = 0u; step < maxSteps && s < ray.end; step++)
    {
        // A product by 2^−level is the quotient by 2^level, exactly.
        float side = exactPowerOfTwo(int(level));
        vec2 cell = floor(pixel * exactPowerOfTwo(-int(level)));
        float exit = cellExit(ray, inverse, cell, side, farSide, crossed);
        if (level == 0u && meetsPixelSurface(ray, inverse, cell, travel, hit, entryShare))
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
        // The walk still moves on, into the cell across the edges that the image leaves through.
        float crossing = max(s, exit);
        float next = meetsSlab ? entry : crossing;
        // After a miss, the pixel of the cell across that the image enters. After a descent to an entry past s, the
        // cell's pixel that the image leaves there, because the march tests a pixel's stretch up to its end, where the
        // pixel's slab can reach the ray. That is the pixel at the float just below the entry: no edge lies between
        // them. An entry at s keeps the pixel, which s can stand on the edge of, so the select drops the pixel of an
        // entry at s = 0, whose float below holds no number.
        float pixelS = meetsSlab ? floatBelow(entry) : crossing;
        vec2 nextPixel = pixelAt(ray, inverse, meetsSlab ? cell : cell + travel * crossed, side, travel, pixelS);
        pixel = !meetsSlab || entry > s ? nextPixel : pixel;
        // After a descent, the slab bound that s was solved for; min and max keep it defined where the cell is missed.
        inW = meetsSlab ? min(max(inW, low), high) : ray.startW + ray.stepW * crossing;
        s = next;
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

#endif
