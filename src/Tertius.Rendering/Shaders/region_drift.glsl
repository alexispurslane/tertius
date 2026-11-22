// The region's drift (docs/plans/bounce-light-confidence.md, section 8.7; ticket t0-acxm): the second source of a block's
// light evidence. The test runs at anchor blocks only, every AnchorStepBlocks-th block in x and in y: the regions of two
// neighbor blocks share 272 of their 289 taps, so their answers are nearly one answer. An anchor's workgroup of the
// spatial dispatch sums the taps of its region, keeps the anchor's ring of its region's light and tests whether that light
// moved (updateRegionAnchor). Every block then takes the answers of the draw before from the anchors whose regions hold
// it (regionAnchorMovedBefore). The tap's layout, the ring's layout, the validity rule and the test live only here: the
// spatial dispatch calls the two functions. Only the bounce light runs the test and keeps a ring
// (TemporalSignal.TestsRegionDrift). Needs accumulate.glsl.
#ifndef TERTIUS_REGION_DRIFT_GLSL
#define TERTIUS_REGION_DRIFT_GLSL

#include "meter.glsl"

// The draws of each of the test's two windows, W: the jitter's 18 phases under FSR at the render scale 2/3, so that a
// cycle of the jitter cancels in each window.
const uint RegionWindowDraws = 18u;

// The draws that an anchor's ring keeps: the newer and the older window.
const uint RegionRingDraws = 2u * RegionWindowDraws;

// The region of an anchor: the blocks up to this far in x and in y, 17 × 17 blocks.
const int RegionRadiusBlocks = 8;

// The side of a region, in blocks.
const int RegionSide = 2 * RegionRadiusBlocks + 1;

// The anchors' step in blocks, in x and in y. With the step equal to the radius, the regions of neighbor anchors overlap
// by half, and each block lies in the region of 4 anchors, or of up to 9 on the anchors' own rows and columns.
const int AnchorStepBlocks = RegionRadiusBlocks;

// The tolerance of the test, in standard deviations of the older window's spread of the region's light.
const float DriftSigmas = 3.0;

// The pixel of a block whose sample is the block's tap, in the block's own pixels.
const ivec2 RegionTapPixel = ivec2(4);

// Each anchor's ring (TemporalSignal.RegionRingFormat), one texel per anchor: layer k holds the draw whose regionSlot is
// k, r the region's light of that draw, g the count of valid draws in a row up to it, at most RegionRingDraws, 0 for a
// draw that is not valid, and b 1 where the test found that the region's light moved in that draw, else 0. Only the
// anchor's workgroup writes it, and only into this draw's layer.
layout(set = 3, binding = 13, rgba32f) uniform image2DArray regionRing;

// Whether a block is an anchor.
bool isRegionAnchor(ivec2 block)
{
    return all(equal(block % AnchorStepBlocks, ivec2(0)));
}

// The ring texel of an anchor block.
ivec2 ringTexelOf(ivec2 anchorBlock)
{
    return anchorBlock / AnchorStepBlocks;
}

// The ring layer of the draw before this one.
int layerBefore()
{
    return int((parameters.regionSlot + RegionRingDraws - 1u) % RegionRingDraws);
}

// Whether this draw continues the anchors' draws of the draw before: no reset, and the same render size. A change of the
// render size moves the scene points under the taps and changes the anchors that the dispatch covers, so the count of
// valid draws restarts, and no block reads the answers of the draw before.
bool regionContinues()
{
    return parameters.reset == 0u && all(equal(frame.previousViewport.xy, frame.viewport.xy));
}

// The last anchor that this draw's dispatch covers, in blocks. The ring has the size of the largest render size, so its
// texels beyond this anchor hold no answer of the draw before.
ivec2 lastRegionAnchor()
{
    ivec2 blocks = (ivec2(frame.viewport.xy) + 7) / 8;
    return (blocks - 1) / AnchorStepBlocks;
}

// The anchors that can hold a block in their regions: up to 3 in x and 3 in y, so that each candidate's read can run in
// its own invocation.
const uint RegionCandidates = 9u;

// Whether the anchor `candidate` (0 to RegionCandidates − 1, row by row) of the anchors around `block` holds the block in
// its region and saw its region's light move in the draw before. False for a candidate that does not exist: one outside
// the images, or one whose region does not reach the block. A block has region evidence where any candidate says yes, so
// the caller merges the 9 answers with a maximum, which does not depend on their order. The answer acts one draw late, so
// that no block reads a layer that a workgroup writes in this draw: the windows are 18 draws long, so one draw does not
// matter. A draw that does not continue the draw before (regionContinues) reads no answer.
bool regionAnchorMovedBefore(ivec2 block, uint candidate)
{
    if (!regionContinues() || candidate >= RegionCandidates)
        return false;
    ivec2 first = max((block - RegionRadiusBlocks + AnchorStepBlocks - 1) / AnchorStepBlocks, ivec2(0));
    ivec2 anchor = first + ivec2(int(candidate % 3u), int(candidate / 3u));
    ivec2 last = min((block + RegionRadiusBlocks) / AnchorStepBlocks, lastRegionAnchor());
    if (any(greaterThan(anchor, last)))
        return false;
    return imageLoad(regionRing, ivec3(anchor, layerBefore())).b != 0.0;
}

// One draw of an anchor's ring.
struct RegionDraw
{
    float light;        // the region's light of the draw: the mean of its counted taps
    float validDraws;   // the count of valid draws in a row up to the draw, at most RegionRingDraws, 0 if it is not valid
};

// The invocations that add a part of the region's sums in the second step of updateRegionAnchor, and the first step's
// slots that each adds.
const uint RegionAdders = 8u;
const uint RegionSlotsPerAdder = 64u / RegionAdders;

// The ring's draws that one invocation adds in the second step: 3 of the older window's 18, or of the newer window's 17
// draws before this one.
const uint RegionDrawsPerAdder = 3u;
const uint RegionWindowAdders = RegionWindowDraws / RegionDrawsPerAdder;

// Each invocation's share of the region's sums in the first step: x the counted light, y the counted taps, z the taps
// inside the images.
shared vec3 regionShares[64];

// The anchor's ring, layer by layer, as the draw before left it: r the light, g the count of valid draws.
shared vec2 regionRingBefore[RegionRingDraws];

// The second step's parts: the region's sums, then the older window's sum and sum of squares of its draws less the older
// window's first draw, then the newer window's sum of its draws before this one less that same draw.
shared vec3 regionParts[RegionAdders];
shared vec2 regionOlderParts[RegionWindowAdders];
shared float regionNewerParts[RegionWindowAdders];

// The tap of `block` in this draw: x the luminance of its tap pixel's sample over the draw's exposure scale, so that a
// change of the exposure alone leaves it, y 1 where the tap counts, else 0, and z 1 where the tap lies inside the images.
// A tap counts where its pixel shows a surface that does not move on screen, and holds a finite sample: a moving
// surface shows another surface point in each draw. The sample is the producer's, before the temporal dispatch's
// limit of the incoming estimate, a prototype that is off by default.
vec3 regionTapOf(ivec2 block)
{
    ivec2 pixel = 8 * block + RegionTapPixel;
    if (!insideImages(pixel))
        return vec3(0.0);
    vec3 sampled = imageLoad(sampleImage, pixel).rgb;
    bool counts = texelFetch(gbufferDistance, pixel, 0).r > 0.0
        && finiteSample(sampled)
        && (imageLoad(surfaceTest, pixel).r & SurfaceMovesOnScreen) == 0u;
    return counts ? vec3(luminance(sampled) / exposureScale(), 1.0, 1.0) : vec3(0.0, 0.0, 1.0);
}

// The region's light of this draw from its `sums`: the mean of its counted taps. The draw is valid where at least half of
// the region's taps inside the images count, so that the mean is of mostly the same surfaces from draw to draw: a camera
// move makes every tap move, so the count restarts. `validDrawsBefore` is the count of the draw before, 0 where this draw
// does not continue it.
RegionDraw regionDrawOf(vec3 sums, float validDrawsBefore)
{
    bool valid = sums.y > 0.0 && 2.0 * sums.y >= sums.z;
    return RegionDraw(valid ? sums.x / sums.y : 0.0, valid ? min(validDrawsBefore + 1.0, float(RegionRingDraws)) : 0.0);
}

// The ring's light of the draw `back` draws before this one, 1 to RegionRingDraws − 1.
float regionLightBefore(uint back)
{
    return regionRingBefore[(parameters.regionSlot + RegionRingDraws - back) % RegionRingDraws].r;
}

// The older window's first draw, which every sum of the test subtracts from each of its draws. So the spread keeps its
// precision when it is small beside the light, and draws of exactly one light give both windows exactly the same mean: a
// difference of rounding cannot pass a tolerance of 0.
float regionOlderFirst()
{
    return regionLightBefore(RegionRingDraws - 1u);
}

// Whether the region's light moved, from this draw `now` and the second step's parts: whether the mean of the newer window
// (this draw and the W − 1 before it) differs from the mean of the older window (the W draws before those) by more than
// DriftSigmas standard deviations of the older window's spread, times sqrt(2 / W), the spread of a difference of two
// means of W draws. False until 2 W valid draws in a row exist. The older window's spread is a measured quantity: a step
// inside the newer window does not raise it.
bool regionTestOf(RegionDraw now)
{
    if (now.validDraws < float(RegionRingDraws))
        return false;
    float newer = now.light - regionOlderFirst();
    vec2 older = vec2(0.0);
    for (uint part = 0u; part < RegionWindowAdders; part++)
    {
        newer += regionNewerParts[part];
        older += regionOlderParts[part];
    }
    float window = float(RegionWindowDraws);
    float olderSpread = sqrt(max(older.y - older.x * older.x / window, 0.0) / (window - 1.0));
    return abs(newer - older.x) / window > DriftSigmas * olderSpread * sqrt(2.0 / window);
}

// The anchor's workgroup: sums the taps of its region, and writes this draw and the test's answer into its ring. Its
// caller calls it in a branch that is uniform in the workgroup (isRegionAnchor of the workgroup's block), so that its
// two barriers run in uniform control flow and the other workgroups run none of them. The sums run in three steps, each
// in a fixed order: each invocation sums its own taps of the 17 × 17 into its own slot, 8 invocations each add 8 slots
// and 12 invocations add the ring's 35 draws before this one, 3 each and the last 2, and invocation 0 adds those parts.
// So one machine gives the same bits in every run.
void updateRegionAnchor(ivec2 anchorBlock)
{
    uint index = gl_LocalInvocationIndex;
    ivec2 texel = ringTexelOf(anchorBlock);
    vec3 share = vec3(0.0);
    for (uint tap = index; tap < uint(RegionSide * RegionSide); tap += 64u)
        share += regionTapOf(anchorBlock + ivec2(int(tap) % RegionSide, int(tap) / RegionSide) - RegionRadiusBlocks);
    regionShares[index] = share;
    if (index < RegionRingDraws)
        regionRingBefore[index] = imageLoad(regionRing, ivec3(texel, int(index))).rg;
    barrier();
    if (index < RegionAdders)
    {
        vec3 part = vec3(0.0);
        for (uint slot = index * RegionSlotsPerAdder; slot < (index + 1u) * RegionSlotsPerAdder; slot++)
            part += regionShares[slot];
        regionParts[index] = part;
    }
    else if (index < RegionAdders + RegionWindowAdders)
    {
        // The older window: the draws RegionWindowDraws to RegionRingDraws − 1 back.
        uint first = RegionWindowDraws + (index - RegionAdders) * RegionDrawsPerAdder;
        vec2 part = vec2(0.0);
        for (uint back = first; back < first + RegionDrawsPerAdder; back++)
        {
            float apart = regionLightBefore(back) - regionOlderFirst();
            part += vec2(apart, apart * apart);
        }
        regionOlderParts[index - RegionAdders] = part;
    }
    else if (index < RegionAdders + 2u * RegionWindowAdders)
    {
        // The newer window before this draw: the draws 1 to RegionWindowDraws − 1 back.
        uint first = 1u + (index - RegionAdders - RegionWindowAdders) * RegionDrawsPerAdder;
        float part = 0.0;
        for (uint back = first; back < min(first + RegionDrawsPerAdder, RegionWindowDraws); back++)
            part += regionLightBefore(back) - regionOlderFirst();
        regionNewerParts[index - RegionAdders - RegionWindowAdders] = part;
    }
    barrier();
    if (index == 0u)
    {
        vec3 sums = vec3(0.0);
        for (uint part = 0u; part < RegionAdders; part++)
            sums += regionParts[part];
        float before = regionContinues() ? regionRingBefore[layerBefore()].g : 0.0;
        RegionDraw now = regionDrawOf(sums, before);
        bool moved = regionTestOf(now);
        imageStore(regionRing, ivec3(texel, int(parameters.regionSlot)), vec4(now.light, now.validDraws, moved ? 1.0 : 0.0, 0.0));
    }
}

#endif
