// The two-depth walk of the screen tracer (docs/plans/temporal-renderer.md, section 6, "The traversal", decisions 40
// and 45): V6 of the profiling spike over the depth pyramid's finest level and its bounds. walkScreen is the one place of
// the walk's step and loop, for both ray kinds. ScreenRay.Walk copies the geometry, without the hair-card rule.
// AHairReceiversWalkPassesItsOwnCardAndHitsAnotherCard tests that rule on the GPU. Change both geometry copies together.
#ifndef TERTIUS_SCREEN_WALK_GLSL
#define TERTIUS_SCREEN_WALK_GLSL

#include "screen_ray.glsl"

// A walk's outcome.
const uint WalkHit = 1u;
const uint WalkMiss = 2u;

const int WalkTopLevel = 8;   // ScreenRay.WalkTopLevel

// What a walk keeps of its ray from its start to its end.
struct ScreenWalk
{
    ScreenRay ray;
    vec3 direction;            // the ray's unit direction in the camera-relative axes, for the facing test
    RayReciprocals inverse;
    vec2 travel;               // the sign of the image's step on each axis
    vec2 farSide;              // max(travel, 0)
    uint ownCard;              // the card code of a hair receiver's own card, or NoCard
};

// The walk of the image of a ray along `direction`, from a receiver whose own hair card is `ownCard`, or NoCard.
ScreenWalk screenWalkOf(ScreenRay ray, vec3 direction, uint ownCard)
{
    ScreenWalk walk;
    walk.ray = ray;
    walk.direction = direction;
    walk.inverse = reciprocalsOf(ray);
    walk.travel = sign(ray.step);
    walk.farSide = max(walk.travel, 0.0);
    walk.ownCard = ownCard;
    return walk;
}

// The cell of `level` inside its parent of the level above where the image stands at s (ScreenRay.ChildAt), by the
// parameters where the image reaches the parent's center lines, which the crossings compare too. At an s that a descent
// solved as a slab's entry (`leaving`), an s on a center line stands in the cell that the image leaves there; at an s
// that a crossing reached, in the cell that it enters.
vec2 childAt(ScreenWalk walk, vec2 parent, int level, float s, bool leaving)
{
    float side = exactPowerOfTwo(level);
    float reached = leaving ? floatBelow(s) : s;
    vec2 center = edgeParameters(walk.ray, walk.inverse, (parent * 2.0 + 1.0) * side, NoEnd);
    bvec2 passed = lessThanEqual(center, vec2(reached));
    vec2 second = vec2(equal(passed, greaterThan(walk.travel, vec2(0.0))));
    vec2 still = clamp(floor(walk.ray.start * exactPowerOfTwo(-level)), parent * 2.0, parent * 2.0 + 1.0);
    return mix(parent * 2.0 + second, still, equal(walk.travel, vec2(0.0)));
}

// Whether a pixel that the ray meets at the finest level is a hit: its surface faces the ray, and it is not a pixel of
// the receiver's own hair card, which the ray passes through.
bool hitsPixel(ScreenWalk walk, ivec2 pixel)
{
    if (dot(octDecode(texelFetch(gbufferNormal, pixel, 0).ba), walk.direction) >= 0.0)
        return false;
    if (walk.ownCard == NoCard)
        return true;
    uvec4 shading = texelFetch(gbufferShading, pixel, 0);
    return cardOfWords(shading.b, shading.a) != walk.ownCard;
}

// Walks the ray through the pyramid to its first hit, its end or `cap` steps, one texel read per step: the finest level,
// R32F, the center w of each pixel, and the bounds, RG32F, whose mip m holds level m + 1. A cell is a slab: from its far
// bound times 1 / (1 + κ) to its near bound, or at the finest level from w / (1 + κ) to w. A ray that passes in front of
// the slab or behind it crosses the cell and goes up a level, one that meets the slab of a level above 0 goes down to
// where it enters it, and one that meets a pixel's slab hits where hitsPixel holds. A walk that reaches its cap is a miss.
// On a hit, gives the hit's position in pixels, its pixel and the entry share e, 0 at the slab's front and 1 at its back.
uint walkScreen(texture2D finest, texture2D bounds, ScreenWalk walk, uint cap, out vec2 hit, out ivec2 hitPixel,
    out float entryShare)
{
    hit = vec2(0.0);
    hitPixel = ivec2(0);
    entryShare = 0.0;
    vec2 crossed;
    float s = cellExit(walk.ray, walk.inverse, floor(walk.ray.start), 1.0, walk.farSide, crossed);
    // The cell that the walk stands in, at its level: the pixel past the origin's pixel to start with.
    vec2 cell = floor(walk.ray.start) + walk.travel * crossed;
    int level = 0;
    int top = min(textureQueryLevels(bounds), WalkTopLevel);
    const float backShare = 1.0 / (1.0 + ThicknessShare);
    // Whether a descent solved s as a slab's entry, rather than a crossing reaching it on a cell's edge.
    bool solved = false;
    for (uint steps = 0u; steps < cap; steps++)
    {
        if (s >= walk.ray.end)
            return WalkMiss;
        float side = exactPowerOfTwo(level);
        float exit = max(cellExit(walk.ray, walk.inverse, cell, side, walk.farSide, crossed), s);
        // A cell beyond the screen holds no surface. Its fetch would leave the active part.
        vec2 cellBounds = vec2(0.0, EmptyFar);
        if (!beyondTheScreen(cell, side))
            cellBounds = level == 0 ? vec2(texelFetch(finest, ivec2(cell), 0).r)
                : texelFetch(bounds, ivec2(cell), level - 1).rg;
        float near = cellBounds.x;
        float back = cellBounds.y * backShare;
        float inW = walk.ray.startW + walk.ray.stepW * s;
        float outW = walk.ray.startW + walk.ray.stepW * exit;
        bool inFront = near <= 0.0 || min(inW, outW) > near;
        bool behind = !inFront && max(inW, outW) < back;
        bool meetsSlab = !inFront && !behind;
        // min and max, not clamp: exit can lie a rounding below s, and clamp is undefined when its minimum passes its
        // maximum.
        float entry = min(max(slabEntry(walk.ray, walk.inverse, s, inW, back, near), s), exit);
        if (meetsSlab && level > 0)
        {
            level--;
            solved = solved || entry > s;
            cell = childAt(walk, cell, level, entry, solved);
            s = entry;
            continue;
        }
        if (meetsSlab && hitsPixel(walk, ivec2(cell)))
        {
            hit = clamp(walk.ray.start + walk.ray.step * entry, cell, cell + 1.0);
            hitPixel = ivec2(cell);
            // A thickness that rounds to none leaves a slab of no depth, whose share would be 0 / 0.
            entryShare = back < near ? entryShareOf(inW, back, near) : 0.0;
            return WalkHit;
        }
        // Across the cell and up a level: in front of its slab, behind it, or past a pixel that is no hit.
        s = exit;
        solved = false;
        cell += walk.travel * crossed;
        if (level < top)
        {
            level++;
            cell = floor(cell * 0.5);
        }
    }
    return WalkMiss;
}

#endif
