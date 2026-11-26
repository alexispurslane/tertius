// The region's drift (https://github.com/alexispurslane/tertius/wiki/bounce-light-confidence, section 8.7; ticket t0-acxm): the second source of a block's
// light evidence. The test runs at anchor blocks only, every AnchorStepBlocks-th block in x and in y: the regions of two
// neighbor blocks share 272 of their 289 taps, so their answers are nearly one answer. An anchor's workgroup of the
// spatial dispatch sums the taps of its region, keeps the anchor's ring of its region's light and tests whether that light
// moved (updateRegionAnchor). Every block then takes the answers of the draw before from the anchors whose regions hold
// it (regionAnchorMovedBefore). The tap's layout, the ring's layout, the validity rule, the change gate and both tests
// live only here: the spatial dispatch calls the two functions. Only the bounce light runs the test and keeps a ring
// (TemporalSignal.TestsRegionDrift). Needs accumulate.glsl, and evidenceOfSample and NoEvidence of
// accumulate_spatial.comp, which includes this file right after them.
//
// The change gate (ticket t1-yi11) chooses the test. A region's light changes after a cause: a light changed, a surface
// moved, or the view moved. While a cause is recent the gate is open, and the open gate's test answers (regionTestSignOf). A
// frozen view has no cause, so the gate is closed, and a stricter test answers (regionClosedDecisionOf): Welch's t of
// the two windows must pass a high bar, or a lower bar beside a neighbor anchor whose answer of the draw before has the
// same sign. Its false answers then stay rare and do not grow into patches.
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

// The tolerance of the open gate's test, in standard deviations of the older window's spread of the region's light.
const float DriftSigmas = 3.0;

// The draws for which the gate stays open after a cause (TemporalSignal.RegionCauseWindowDraws): the bounce light's cap,
// the draws in which a history that a cut shortened grows back and the later bounces settle.
const float RegionCauseWindowDraws = 128.0;

// The gate of an anchor whose view is not yet established: no cause, and fewer than FullRateFrames valid draws in a row
// since its draws started. Its gate is closed. A start then never counts as a recovery from a move: a pixel's history
// holds no stale light before it is longer than FullRateFrames.
const float RegionGateUnestablished = -1.0;

// The closed gate's bars, in Welch's t of the two windows. A change at the seed bar answers alone. A change at the confirm
// bar answers only beside a same-sign answer of the draw before, at this anchor or at one of its 8 neighbors. The bars
// come from a replay of the frozen bay's and the still cabin's logged region lights (t1-yi11): they are measured on
// correlated draws, not a guaranteed false rate.
const float WelchSeedBar = 5.0;
const float WelchConfirmBar = 2.6;

// The pixel of a block whose sample is the block's tap, in the block's own pixels.
const ivec2 RegionTapPixel = ivec2(4);

// Each anchor's ring (TemporalSignal.RegionRingFormat), one texel per anchor: layer k holds the draw whose regionSlot is
// k, r the region's light of that draw, g the count of valid draws in a row up to it, at most RegionRingDraws, 0 for a
// draw that is not valid, b the signed answer of that draw: 1 or −1 where the light moved up or down, else 0, and a the
// change gate after that draw (regionGateAfter): RegionGateUnestablished, 0 to RegionCauseWindowDraws − 1 for an open
// gate, or RegionCauseWindowDraws for a closed one. Only the anchor's workgroup writes it, and only into this draw's
// layer.
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
// its region and saw its region's light move in the draw before, up or down. False for a candidate that does not exist: one outside
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
// inside the images, w the counted taps that bring a cause.
shared vec4 regionShares[64];

// The anchor's ring, layer by layer, as the draw before left it: x the light, y the count of valid draws, z the gate.
shared vec3 regionRingBefore[RegionRingDraws];

// The second step's parts: the region's sums, then the older window's sum and sum of squares of its draws less the older
// window's first draw, then the same of the newer window's draws before this one.
shared vec4 regionParts[RegionAdders];
shared vec2 regionOlderParts[RegionWindowAdders];
shared vec2 regionNewerParts[RegionWindowAdders];

// The tap of `block` in this draw: x the luminance of its tap pixel's sample over the draw's exposure scale, so that a
// change of the exposure alone leaves it, y 1 where the tap counts, else 0, z 1 where the tap lies inside the images, and
// w 1 where the tap counts and its sample brings a cause, else 0. A tap counts where its pixel shows a surface that does
// not move on screen, and holds a finite sample: a moving surface shows another surface point in each draw. The sample
// is the producer's, before the temporal dispatch's limit of the incoming estimate, a prototype that is off by default.
// A cause is evidence of the sample's own rays (evidenceOfSample): a recent light code or the motion bit.
vec4 regionTapOf(ivec2 block)
{
    ivec2 pixel = 8 * block + RegionTapPixel;
    if (!insideImages(pixel))
        return vec4(0.0);
    vec3 sampled = imageLoad(sampleImage, pixel).rgb;
    bool counts = texelFetch(gbufferDistance, pixel, 0).r > 0.0
        && finiteSample(sampled)
        && (imageLoad(surfaceTest, pixel).r & SurfaceMovesOnScreen) == 0u;
    if (!counts)
        return vec4(0.0, 0.0, 1.0, 0.0);
    bool cause = evidenceOfSample(imageLoad(sampleAge, pixel).r) != NoEvidence;
    return vec4(luminance(sampled) / exposureScale(), 1.0, 1.0, cause ? 1.0 : 0.0);
}

// The region's light of this draw from its `sums`: the mean of its counted taps. The draw is valid where at least half of
// the region's taps inside the images count, so that the mean is of mostly the same surfaces from draw to draw: a camera
// move makes every tap move, so the count restarts. `validDrawsBefore` is the count of the draw before, 0 where this draw
// does not continue it.
RegionDraw regionDrawOf(vec4 sums, float validDrawsBefore)
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

// The change gate after this draw `now` (the table of t1-yi11's production design), from whether this draw continues the
// draw before, the gate of the draw before and whether a counted tap brought a cause. A cause opens the gate, also in a
// draw that does not continue. A draw that does not continue (a reset or a new render size) starts unestablished: its
// draws before are gone, so nothing holds the gate open. An unestablished anchor becomes established and closed after
// FullRateFrames valid draws in a row. An established anchor whose draw is not valid lost the view that its pixels'
// histories hold, as a camera move does: the gate opens and stays at 0 while the move lasts. Else an open gate counts
// its draws and closes at RegionCauseWindowDraws.
float regionGateAfter(bool continues, float gateBefore, RegionDraw now, bool sawCause)
{
    if (sawCause)
        return 0.0;
    if (!continues)
        return RegionGateUnestablished;
    if (gateBefore < 0.0)
        return now.validDraws >= FullRateFrames ? RegionCauseWindowDraws : RegionGateUnestablished;
    if (now.validDraws == 0.0)
        return 0.0;
    return min(gateBefore + 1.0, RegionCauseWindowDraws);
}

// The sums of the two windows' draws less the older window's first draw: x the sum and y the sum of squares of the
// older window (the 2 W − 1 to W draws back), and of the newer window (this draw and the W − 1 before it).
struct RegionMoments
{
    vec2 older;
    vec2 newer;
};

// The moments of the two windows from this draw `now` and the second step's parts, added in a fixed order.
RegionMoments regionMomentsOf(RegionDraw now)
{
    float apart = now.light - regionOlderFirst();
    RegionMoments moments = RegionMoments(vec2(0.0), vec2(apart, apart * apart));
    for (uint part = 0u; part < RegionWindowAdders; part++)
    {
        moments.newer += regionNewerParts[part];
        moments.older += regionOlderParts[part];
    }
    return moments;
}

// The open gate's test: the sign of the move where the mean of the newer window differs from the mean of the older
// window by more than DriftSigmas standard deviations of the older window's spread, times sqrt(2 / W), the spread of a
// difference of two means of W draws, else 0. The older window's spread is a measured quantity: a step inside the newer
// window does not raise it.
float regionTestSignOf(RegionMoments moments)
{
    float window = float(RegionWindowDraws);
    float olderSpread = sqrt(max(moments.older.y - moments.older.x * moments.older.x / window, 0.0) / (window - 1.0));
    float difference = moments.newer.x - moments.older.x;
    return abs(difference) / window > DriftSigmas * olderSpread * sqrt(2.0 / window) ? sign(difference) : 0.0;
}

// Welch's t of the newer window against the older one: (m_new − m_old) / sqrt((s_new² + s_old²) / W), with each
// window's own unbiased variance. Both spreads count, so a single bright draw cannot pass a high bar: it raises the
// spread of its own window with the mean. Where both spreads are 0, a difference of the means is a change of
// any size: ±1e9 with its sign, and 0 for no difference, so that no division by 0 happens.
float welchT18Of(RegionMoments moments)
{
    float window = float(RegionWindowDraws);
    float newerMean = moments.newer.x / window;
    float olderMean = moments.older.x / window;
    float newerVariance = max(moments.newer.y - moments.newer.x * newerMean, 0.0) / (window - 1.0);
    float olderVariance = max(moments.older.y - moments.older.x * olderMean, 0.0) / (window - 1.0);
    float spread = sqrt((newerVariance + olderVariance) / window);
    float difference = newerMean - olderMean;
    if (spread > 0.0)
        return difference / spread;
    return sign(difference) * 1.0e9;
}

// The closed gate's answer at the anchor's ring texel `texel` from this draw's Welch's t `t18`: the sign of t at the seed
// bar. At the confirm bar, the sign of t where this anchor or one of its 8 neighbors answered with that sign in the draw
// before, else 0. The neighbors are clamped into the anchors that this draw covers, so no texel beyond them, which can
// hold the answers of a larger render size, counts. Only the layer of the draw before is read, which every workgroup
// finished in the dispatch before. A weak answer so spreads one anchor a draw while the change lasts, and stops where
// the change does. The caller asks only with both windows valid, so this draw continues the draw before.
float regionClosedDecisionOf(ivec2 texel, float t18)
{
    float size = abs(t18);
    if (size >= WelchSeedBar)
        return sign(t18);
    if (size < WelchConfirmBar)
        return 0.0;
    float direction = sign(t18);
    ivec2 last = lastRegionAnchor();
    for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            ivec2 anchor = clamp(texel + ivec2(dx, dy), ivec2(0), last);
            if (direction * imageLoad(regionRing, ivec3(anchor, layerBefore())).b > 0.0)
                return direction;
        }
    return 0.0;
}

// The anchor's signed answer in this draw `now` under the gate `gate`: 0 until 2 W valid draws in a row exist, then the
// open gate's test or the closed gate's.
float regionAnswerOf(ivec2 texel, RegionDraw now, float gate)
{
    if (now.validDraws < float(RegionRingDraws))
        return 0.0;
    RegionMoments moments = regionMomentsOf(now);
    if (gate >= 0.0 && gate < RegionCauseWindowDraws)
        return regionTestSignOf(moments);
    return regionClosedDecisionOf(texel, welchT18Of(moments));
}

// The anchor's workgroup: sums the taps of its region, and writes this draw and the test's answer into its ring. Its
// caller calls it in a branch that is uniform in the workgroup (isRegionAnchor of the workgroup's block), so that its
// two barriers run in uniform control flow and the other workgroups run none of them. The sums run in three steps, each
// in a fixed order: each invocation sums its own taps of the 17 × 17 into its own slot, 8 invocations each add 8 slots
// and 12 invocations add the ring's 35 draws before this one and their squares, 3 each and the last 2, and invocation 0
// adds those parts. So one machine gives the same bits in every run.
void updateRegionAnchor(ivec2 anchorBlock)
{
    uint index = gl_LocalInvocationIndex;
    ivec2 texel = ringTexelOf(anchorBlock);
    vec4 share = vec4(0.0);
    for (uint tap = index; tap < uint(RegionSide * RegionSide); tap += 64u)
        share += regionTapOf(anchorBlock + ivec2(int(tap) % RegionSide, int(tap) / RegionSide) - RegionRadiusBlocks);
    regionShares[index] = share;
    if (index < RegionRingDraws)
        regionRingBefore[index] = imageLoad(regionRing, ivec3(texel, int(index))).rga;
    barrier();
    if (index < RegionAdders)
    {
        vec4 part = vec4(0.0);
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
        vec2 part = vec2(0.0);
        for (uint back = first; back < min(first + RegionDrawsPerAdder, RegionWindowDraws); back++)
        {
            float apart = regionLightBefore(back) - regionOlderFirst();
            part += vec2(apart, apart * apart);
        }
        regionNewerParts[index - RegionAdders - RegionWindowAdders] = part;
    }
    barrier();
    if (index == 0u)
    {
        vec4 sums = vec4(0.0);
        for (uint part = 0u; part < RegionAdders; part++)
            sums += regionParts[part];
        bool continues = regionContinues();
        vec3 before = regionRingBefore[layerBefore()];
        RegionDraw now = regionDrawOf(sums, continues ? before.y : 0.0);
        float gate = regionGateAfter(continues, before.z, now, sums.w > 0.0);
        float answer = regionAnswerOf(texel, now, gate);
        imageStore(regionRing, ivec3(texel, int(parameters.regionSlot)), vec4(now.light, now.validDraws, answer, gate));
    }
}

#endif
