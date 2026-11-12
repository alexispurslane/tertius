// The block test of an accumulation (docs/plans/bounce-light-confidence.md, section 8): evidence of a change of light
// that no pixel's own samples can tell from their noise. A block is the 8 × 8 pixels of one workgroup of either
// dispatch. The temporal dispatch sums its pixels' differences of the fast history and the own-sample mean into one
// texel of the block sums, and the spatial dispatch tests the windows of 1 × 1, 3 × 3 and 7 × 7 blocks around its
// block. A window fires where its fast mean differs from its own-sample mean by more than the noise of its streams and
// more than the floor; then every contributing pixel of the block has evidence for this draw. This file owns the
// texel's layout and the own-sample mean's images. A dispatch that includes it defines ACCUMULATE_BLOCK_SUMS (the
// temporal dispatch, which writes the sums) or ACCUMULATE_BLOCK_WINDOWS (the spatial dispatch, which reads them), so
// that each declares only its own shared memory.
//
// A stream is the sample that one ray block's ray gives its 2 × 2 pixels in one draw: one independent ray sample. The
// texel of a block holds (Σd, Σv, n, Σs) over its streams that have a contributing pixel, in pre-exposed luminance:
// d = luminance(F) − R, v = the predicted variance of d, n = the count of streams, s = R, each the mean over the
// stream's contributing pixels.
//
// R is the own-sample mean: the luminance mean of the pixel's own samples, blended with the slow history's weight
// 1 / L. The slow history is not that mean: the diffuse filter writes the light that it moves into it, and the
// reflections' narrow clamp moves it, so in a still F − S is not 0 (docs/plans/bounce-light-confidence.md, section 8).
// Only the temporal dispatch blends R, and the spatial dispatch restarts it at luminance(F) on evidence, as it cuts L
// to the fast cap.
//
// Every invocation of a workgroup reaches each barrier in uniform control flow: the dispatches call blockSumsStore and
// blockEvidence from main, outside every branch but the push constant blockTest, which is dynamically uniform. The sums
// run in a fixed order, so one machine gives the same bits in every run. No subgroup operation is used.
#ifndef TERTIUS_ACCUMULATE_BLOCKS_GLSL
#define TERTIUS_ACCUMULATE_BLOCKS_GLSL

// The constants of the test, from the CPU model. Take T3 calibrates them and step 7 sets them
// (docs/plans/bounce-light-confidence.md, section 20). The floor, ChangeShare, is the push constant changeShare, from the
// signal's HistoryConfidence.

// The fewest streams of a window that can fire: half the streams of one block.
const float LeastStreams = 8.0;
// The tail rule: a window fires only where sumS² ≥ TailStreams · sumV, that is n ≥ TailStreams · ρ², with ρ the relative
// spread of one stream's difference. A mean of a heavy tail is close to normal only for many streams (Cochran 1977,
// section 2.15).
const float TailStreams = 1000.0;
// How many predicted deviations the window's difference must lie from 0.
const float BlockSigmas = 4.0;
// The measured over the predicted deviation of a stream's difference, which the hits' reuse of filtered light can raise.
const float BlockNoiseScale = 1.0;

// Set 3, binding 13: the block sums, RGBA32F, one texel per block (TemporalSignal.BlockSums).
#ifdef ACCUMULATE_BLOCK_SUMS
layout(set = 3, binding = 13, rgba32f) uniform writeonly image2D blockSums;
#endif
#ifdef ACCUMULATE_BLOCK_WINDOWS
layout(set = 3, binding = 13, rgba32f) uniform readonly image2D blockSums;
#endif

// Set 3, bindings 16 and 17: the own-sample mean R of the last draw and of this draw, at the slow history's precision
// (TemporalSignal.OwnMean). The temporal dispatch reprojects the last draw's and writes this draw's, and the spatial
// dispatch restarts this draw's on evidence.
#ifdef ACCUMULATE_BLOCK_SUMS
layout(set = 3, binding = 16, SPECULAR_HISTORY_SCALAR_FORMAT) uniform readonly image2D lastOwnMean;
#endif
layout(set = 3, binding = 17, SPECULAR_HISTORY_SCALAR_FORMAT) uniform writeonly image2D ownMeanHistory;

// κ: the deviation of F − R for one stream, as a share of the samples' deviation, where F and R are two exponential
// means of the same independent samples with the weights wf and ws. 0.319 at the bounce cap, 0.063 at a length of 5.
// The max keeps a rounding below 0 near ws = wf out of the root.
float differenceShare(float wf, float ws)
{
    return sqrt(max(wf / (2.0 - wf) + ws / (2.0 - ws) - 2.0 * wf * ws / (wf + ws - wf * ws), 0.0));
}

// A contributing pixel's share of its block's texel: (d, v, 1, s). `ownMean` is its own-sample mean R after the blend,
// `fastLength` its fast cap, `historyLength` its length L after the blend, `deviation` its retained deviation σr after
// the update.
vec4 blockShareOf(float fastLuminance, float ownMean, float deviation, float historyLength, float fastLength)
{
    float kappa = differenceShare(1.0 / fastLength, 1.0 / historyLength) * BlockNoiseScale;
    return vec4(fastLuminance - ownMean, kappa * kappa * deviation * deviation, 1.0, ownMean);
}

#ifdef ACCUMULATE_BLOCK_SUMS
// The shares of the block's 64 pixels, by local invocation index, and the means of its 16 ray blocks.
shared vec4 pixelShares[64];
shared vec4 streamShares[16];

// Sums the block's streams into its texel. `share` is the invocation's blockShareOf, or 0 for a pixel that does not
// contribute: outside the image, the sky, or a length of at most the fast cap. Every workgroup writes its texel, also with
// n = 0, so the spatial dispatch never reads a stale one.
void blockSumsStore(vec4 share)
{
    uint index = gl_LocalInvocationIndex;
    pixelShares[index] = share;
    barrier();
    if (index < 16u)
    {
        // Ray block `index`: the 2 × 2 pixels from (2 (index mod 4), 2 (index / 4)) of the block. Its four pixels see one
        // ray, so they are one stream: the mean of their shares, counted once.
        uint corner = 16u * (index / 4u) + 2u * (index % 4u);
        vec4 sum = pixelShares[corner] + pixelShares[corner + 1u] + pixelShares[corner + 8u] + pixelShares[corner + 9u];
        streamShares[index] = sum.z > 0.0 ? vec4(sum.xy / sum.z, 1.0, sum.w / sum.z) : vec4(0.0);
    }
    barrier();
    if (index == 0u)
    {
        vec4 sums = vec4(0.0);
        for (uint stream = 0u; stream < 16u; stream++)
            sums += streamShares[stream];
        imageStore(blockSums, ivec2(gl_WorkGroupID.xy), sums);
    }
}
#endif

#ifdef ACCUMULATE_BLOCK_WINDOWS
// The texels of the 7 × 7 blocks around the workgroup's block, row by row, 0 outside the image, and whether one of its
// windows fired.
shared vec4 windowTexels[49];
shared uint windowFired;

// Whether a window of the sums (Σd, Σv, n, Σs) fires: enough streams for its tail, a difference BlockSigmas predicted
// deviations from 0, at least the share `changeShare` of the larger of the fast light and the own-sample mean, and
// above the cutoff light. Without a widened box (`retainedSigmas` 0), only a drop counts (section 9): the narrow box's
// lower edge and the anti-lag follow a rise. Products and comparisons only: a zero deviation or light gives no NaN.
bool windowFires(vec4 window, float changeShare, float retainedSigmas)
{
    float sumD = window.x;
    float sumV = window.y;
    float n = window.z;
    float sumS = window.w;
    return n >= LeastStreams
        && sumS * sumS >= TailStreams * sumV
        && sumD * sumD >= BlockSigmas * BlockSigmas * sumV
        && abs(sumD) >= changeShare * max(sumS, sumS + sumD)
        && abs(sumD) >= n * CutoffRadiance
        && (retainedSigmas > 0.0 || sumD < 0.0);
}

// Whether a window of 1 × 1, 3 × 3 or 7 × 7 blocks around the workgroup's block fires. The first 49 invocations load one
// texel each, and invocation 0 forms the windows, so the windows cost 49 loads per block.
bool blockEvidence(float changeShare, float retainedSigmas)
{
    uint index = gl_LocalInvocationIndex;
    if (index < 49u)
    {
        ivec2 texel = ivec2(gl_WorkGroupID.xy) + ivec2(int(index % 7u) - 3, int(index / 7u) - 3);
        bool inside = all(greaterThanEqual(texel, ivec2(0))) && all(lessThan(texel, imageSize(blockSums)));
        windowTexels[index] = inside ? imageLoad(blockSums, texel) : vec4(0.0);
    }
    barrier();
    if (index == 0u)
    {
        vec4 middle = vec4(0.0);
        vec4 wide = vec4(0.0);
        for (int row = 0; row < 7; row++)
            for (int column = 0; column < 7; column++)
            {
                vec4 texel = windowTexels[7 * row + column];
                wide += texel;
                if (abs(row - 3) <= 1 && abs(column - 3) <= 1)
                    middle += texel;
            }
        bool fired = windowFires(windowTexels[24], changeShare, retainedSigmas)
            || windowFires(middle, changeShare, retainedSigmas) || windowFires(wide, changeShare, retainedSigmas);
        windowFired = fired ? 1u : 0u;
    }
    barrier();
    return windowFired != 0u;
}
#endif

#endif
