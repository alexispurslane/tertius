// The shadows of the lights (docs/plans/local-light-shadows.md): the cascade sets of the far lights (ShadowCascades.cs,
// ShadowPass.cs), the far shadows of each far light, and the tiles of the near lights (ShadowTiles.cs, LocalShadowPass.cs).
// Needs frame.glsl and gbuffer_inputs.glsl.
//
// The main view's cascade selection is by view depth. Over the last 10% of each cascade the result blends into the next cascade,
// and the last cascade fades out to fully lit at 2 km, so there are no shadow-map shadows beyond it.
//
// Filtering: 4 × 4 hardware-PCF taps one texel apart. Each tap is a bilinear 2 × 2 comparison, so together they
// cover 5 × 5 texels with a smooth tent-like weight, and reach 2.5 texels from the lookup (ShadowBias.FilterReachTexels).
// Past a caster's edge the map holds no caster, and the map's edge lies within half a texel of the caster's real edge,
// so the filter's lit fraction under the real edge is up to DoubtfulLitFraction. So the cascades' sun share is
// lightOutsideShadow of the lit fraction, 0 up to DoubtfulLitFraction: the blur lies on the lit side of each edge and
// puts no light inside a shadow (t8-rjno, t1-gout). The tiles compute their filter with exact weights, from nine
// gathers or from sixteen reads at texel corners (tileLitFraction), and keep the symmetric filter (tileShadow).
//
// Bias, the same model for the cascades and the tiles (docs/plans/local-light-shadows.md, decision 33), in parts that
// each scale with the texel size:
//  - The shadow passes add a slope-scaled depth bias to the casters, ShadowBias.SlopeBias. Every tap compares against
//    the receiver's own depth, and the filter compares texels up to its reach, 2.5 texels, from the lookup along each
//    texel axis, so the bias covers the receiver's slope over that reach along an axis. On a slope diagonal to the
//    texel grid the corner texels lie up to 2.5 texels off along both axes, and Vulkan lets the slope be the larger of
//    the two axes' slopes, so their comparisons can fall a little short: at most about 2% of the taps at extreme
//    grazing angles, which the cascades' lit-side rule doubles (ARCHITECTURE.md, "Shadow bias").
//    No tap compares against the receiver's plane extrapolated toward the light: at a grazing light, that plane passes
//    under a wall that stands on the receiver, and the taps saw the light past the wall (ter-kn1d).
//  - A normal offset of one texel, scaled by sin(angle to the light), frame.shadowParams.y, which the batcher writes in
//    every frame for the cascades and the tiles alike, plus half a texel of constant depth bias for the cascades.
#ifndef TERTIUS_SHADOWS_GLSL
#define TERTIUS_SHADOWS_GLSL

#include "cube_faces.glsl"

// ShaderConstants.TileCornerMeans: whether tileLitFraction reads a tile's texels as sixteen corner means, or as nine
// gathers.
layout(constant_id = 11) const bool tileCornerMeans = false;

// The comparison of the cascades' map with a receiver at depth `reference`, at `coordinate` in layer `layer`.
float cascadeComparison(vec2 coordinate, float layer, float reference)
{
    // Explicit zero gradients (mip 0): the calling code branches per pixel, where implicit derivatives are undefined.
    return textureGrad(sampler2DArrayShadow(shadowCascades, shadowSampler), vec4(coordinate, layer, reference),
        vec2(0.0), vec2(0.0));
}

// The bilinear comparisons of the shadow filter along one axis, one texel apart, in the cascades and the tiles. One
// texel of a straight edge's travel across the filter changes its lit fraction by 1 / FilterTapsPerAxis. A change of it
// changes three more places with it: ShadowBias.FilterReachTexels, (FilterTapsPerAxis + 1) / 2, which sizes the biases
// and the maps' margins; tileLitFraction's reads, written for 4 taps; and CascadeDoubtfulLitFraction in ShadowTests.
const int FilterTapsPerAxis = 4;

// The lit fraction of the cascade's texels around `coordinate` for a receiver at depth `reference`: the
// FilterTapsPerAxis × FilterTapsPerAxis bilinear comparisons one texel apart.
float filteredCascade(vec2 coordinate, float layer, float reference)
{
    vec2 stride = vec2(frame.shadowParams.x);
    const float center = 0.5 * float(FilterTapsPerAxis - 1);
    float lit = 0.0;
    for (int y = 0; y < FilterTapsPerAxis; y++)
    {
        for (int x = 0; x < FilterTapsPerAxis; x++)
            lit += cascadeComparison(coordinate + (vec2(float(x), float(y)) - center) * stride, layer, reference);
    }
    return lit / float(FilterTapsPerAxis * FilterTapsPerAxis);
}

// The largest lit fraction of a lookup that a caster can still cover (t1-gout): one half for the filter's blur, which is
// half lit at the map's edge, plus the half texel by which the map's edge can lie inside the caster's real edge, at the
// filter's change of 1 / FilterTapsPerAxis per texel. A map texel holds a caster only if the caster covers the texel's
// center, so the map's edge lies within half a texel of the real edge. Along an edge at an angle to the grid the
// rounding averages, and the largest lit fraction on the real edge is the same at 45 degrees and less between
// (docs/research/shadow-grid-phase/README.md, "The remedy").
const float DoubtfulLitFraction = 0.5 + 0.5 / float(FilterTapsPerAxis);

// The sun's share at a receiver whose cascade filter is lit by the fraction `litFraction`: 0 up to DoubtfulLitFraction,
// then rising to 1. So the filter's blur and the rounding of the map's edge to texel centers lie on the lit side of the
// caster's real edge, and no sunlight falls inside a shadow at any place of the texel grid (t8-rjno, the user's choice of
// 2026-11-21; t1-gout). A lookup u texels past the map's edge of a straight caster is lit by 1/2 + u / FilterTapsPerAxis
// up to u = FilterTapsPerAxis / 2. With 4 taps the share is 0 up to u = 1/2 and rises to 1 over the next 1.5 texels: a lit
// area loses 1.25 texels at each edge, a quarter texel more than a rule that starts at one half. The rule trades light
// under partly covered receivers for no leak at a rounded edge, so a caster narrower than 1.5 texels casts 8/3 of its
// true shadow (t1-gout, "A stated limit of the rule"). One bilinear comparison showed a crawling staircase.
//
// The rule is clamp((litFraction - D) / (1 - D), 0, 1), with D = DoubtfulLitFraction = 5/8. The code writes it in two
// steps only for the cost on Apple's M1, and the two steps give the same share to within 6e-8 for every litFraction in
// [0, 1]. The first step, 2 litFraction - 2 D, has factors that the M1 encodes in the instruction, and its clamp at 1
// never binds, because litFraction - D is at most 1 - D = 3/8. The second clamp never binds either: the product is at
// most 1. It stays because the M1 saturates for free. The rule in one step needs two constants in registers at each of the 24 lookups
// of direct_key_fill.frag. That raised the shader from register 115 to 116, where the M1 keeps 384 resident threads
// instead of 448, and cost 0.13 ms to 0.28 ms of the direct pass in every view, with sun or without (t1-gout, "The
// cost"; t2-x53j).
float lightOutsideShadow(float litFraction)
{
    float twice = clamp(2.0 * litFraction - 2.0 * DoubtfulLitFraction, 0.0, 1.0);
    return clamp(twice * (0.5 / (1.0 - DoubtfulLitFraction)), 0.0, 1.0);
}

// The sun's share from one cascade of one cascade set at a world position, with geometricNormal and sinTheta as in
// cascadeShadow. Each cascade applies lightOutsideShadow before cascadeShadow blends two of them.
float sampleCascade(uint set, uint cascade, vec3 position, vec3 geometricNormal, float sinTheta)
{
    float texel = frame.shadowTexel[cascade];
    vec3 lookup = position + geometricNormal * (frame.shadowParams.y * texel * sinTheta);
    vec4 clip = frame.cascadeSets[set].viewProj[cascade] * vec4(lookup, 1.0);
    float reference = clip.z + frame.shadowParams.z;
    // ShadowMap.Layer: the layers of a set follow those of the sets before it.
    float layer = float(set * 4u + cascade);
    return lightOutsideShadow(filteredCascade(clip.xy * 0.5 + 0.5, layer, reference));
}

// How far a world position is beyond the shadow maps: 0 where they shadow it, 1 past their end, and a smooth step over
// the last cascade's fade, where the maps' shadow fades out. A far shadow of a caster that the light's own set also draws
// counts by this weight, so the two shadows hand over without a seam. Every set has the same splits.
float beyondShadowMaps(vec3 position)
{
    uint count = frame.counts.z;
    if (count == 0u)
        return 1.0;
    float depth = dot(position, frame.cameraForward.xyz);
    float start = frame.shadowBlendStart[count - 1u], end = frame.shadowSplits[count - 1u];
    return depth >= end ? 1.0 : smoothstep(0.0, 1.0, clamp((depth - start) / (end - start), 0.0, 1.0));
}

// The fraction of a light that its cascade set lets through at a world position. geometricNormal is the interpolated
// vertex normal, which faces the light, and sinTheta the sine of its angle with the direction toward the light: 1 for a
// hair card edge-on to the light.
float cascadeShadow(uint set, vec3 position, vec3 geometricNormal, float sinTheta)
{
    uint count = frame.counts.z;
    if (count == 0u)
        return 1.0;
    // The eye is at the origin of world space for every pass that shades.
    float depth = dot(position, frame.cameraForward.xyz);
    uint cascade = 0u;
    while (cascade < count && depth >= frame.shadowSplits[cascade])
        cascade++;
    if (cascade == count)
        return 1.0;

    float lit = sampleCascade(set, cascade, position, geometricNormal, sinTheta);
    float blendStart = frame.shadowBlendStart[cascade];
    if (depth > blendStart)
    {
        float t = (depth - blendStart) / (frame.shadowSplits[cascade] - blendStart);
        float next = cascade + 1u < count ? sampleCascade(set, cascade + 1u, position, geometricNormal, sinTheta) : 1.0;
        lit = mix(lit, next, smoothstep(0.0, 1.0, t));
    }
    return lit;
}

// The views that are not the main view, the environment faces, pick the finest cascade whose box holds the point
// (docs/plans/environment-map.md, "The shadow maps in a face"). View depth along the camera's forward axis holds
// only for points in the main view's frustum: a point behind the camera has a negative depth, and cascade 0 does not hold
// it once it lies more than a few meters back. Each set has its own boxes, along its own light.

// How far the filter's taps compare from the lookup, in texels: the 5 × 5 texels around the lookup's texel, up to 2
// texels from it. The C# twin is ShadowBias.FilterClearanceTexels.
const float FilterClearanceTexels = 2.0;

// A position in the clip space of a cascade box: xy in [−1, 1] across the box, z in [0, 1] along the light.
vec3 cascadeClip(uint set, uint cascade, vec3 position)
{
    return (frame.cascadeSets[set].viewProj[cascade] * vec4(position, 1.0)).xyz;
}

// How far across a cascade box a clip position lies: 0 at the box's center, 1 at the margin of the filter, more beyond.
// The margin keeps a held point the filter's reach plus the normal offset (frame.shadowParams.y) from the box's edge.
float acrossBox(vec3 clip)
{
    float margin = 2.0 * (FilterClearanceTexels + frame.shadowParams.y) * frame.shadowParams.x;
    return max(abs(clip.x), abs(clip.y)) / (1.0 - margin);
}

// Whether a cascade box holds a clip position with the margin of the filter.
bool boxHolds(vec3 clip)
{
    return acrossBox(clip) <= 1.0 && clip.z >= 0.0 && clip.z <= 1.0;
}

// The finest cascade of a set whose box holds a world position, or frame.counts.z when none does.
uint holdingCascade(uint set, vec3 position)
{
    uint count = frame.counts.z;
    uint cascade = 0u;
    while (cascade < count && !boxHolds(cascadeClip(set, cascade, position)))
        cascade++;
    return cascade;
}

// How far a world position held by the coarsest box alone lies into that box's outer tenth: 0 inside it, to 1 at its edge.
float coarsestBoxFade(uint set, vec3 position)
{
    float across = acrossBox(cascadeClip(set, frame.counts.z - 1u, position));
    return smoothstep(0.0, 1.0, clamp((across - 0.9) / 0.1, 0.0, 1.0));
}

// How far a world position is beyond a set's maps by the finest box that holds it: 0 in a box finer than the coarsest,
// to 1 over the coarsest box's outer tenth, and 1 outside every box. A far shadow of a caster that the light's own set
// also draws counts by this weight, as beyondShadowMaps does for the main view.
float beyondShadowMapsHolding(uint set, vec3 position)
{
    uint count = frame.counts.z;
    if (count == 0u)
        return 1.0;
    uint cascade = holdingCascade(set, position);
    if (cascade == count)
        return 1.0;
    return cascade + 1u == count ? coarsestBoxFade(set, position) : 0.0;
}

// The fraction of a light that its cascade set lets through at a world position, from the finest cascade whose box holds
// it. In the coarsest box's outer tenth the map shadow fades out by the weight of beyondShadowMapsHolding.
// geometricNormal and sinTheta are as in cascadeShadow.
float cascadeShadowHolding(uint set, vec3 position, vec3 geometricNormal, float sinTheta)
{
    uint count = frame.counts.z;
    if (count == 0u)
        return 1.0;
    uint cascade = holdingCascade(set, position);
    if (cascade == count)
        return 1.0;
    float lit = sampleCascade(set, cascade, position, geometricNormal, sinTheta);
    return cascade + 1u == count ? mix(lit, 1.0, coarsestBoxFade(set, position)) : lit;
}

// The fraction of a light's disc (angular radius lightRadius) hidden by an occluder's disc (angular radius
// occluderRadius) whose center is `separation` radians from the light's center.
float discCoverage(float lightRadius, float occluderRadius, float separation)
{
    if (separation >= lightRadius + occluderRadius)
        return 0.0;
    if (occluderRadius > 4.0 * lightRadius)
    {
        // A planet seen from near its surface. Its limb is almost straight across the small disc of the light, so the
        // hidden part is a circular segment. x is the light center's distance outside the limb, in light radii.
        // This form also avoids the float cancellation of the general formula when the radii differ a lot.
        float x = clamp((separation - occluderRadius) / lightRadius, -1.0, 1.0);
        return (acos(x) - x * sqrt(1.0 - x * x)) / PI;
    }
    if (separation <= abs(occluderRadius - lightRadius))
        return occluderRadius >= lightRadius ? 1.0 : (occluderRadius * occluderRadius) / (lightRadius * lightRadius);
    // Two discs of similar size, such as a moon eclipsing the sun: the exact lens area.
    float d = separation, r1 = lightRadius, r2 = occluderRadius;
    float d2 = d * d, r1s = r1 * r1, r2s = r2 * r2;
    float a1 = r1s * acos(clamp((d2 + r1s - r2s) / (2.0 * d * r1), -1.0, 1.0));
    float a2 = r2s * acos(clamp((d2 + r2s - r1s) / (2.0 * d * r2), -1.0, 1.0));
    float k = 0.5 * sqrt(max((-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2), 0.0));
    return clamp((a1 + a2 - k) / (PI * r1s), 0.0, 1.0);
}

// The far shadows of one light: an analytic eclipse test of its disc, of angular radius angularRadius, against the
// bounding sphere of every caster of its occluder range, occluders.x to occluders.x + occluders.y − 1. A sphere shadows
// only points outside it, so a caster never shadows its own surface. The first occluders.z spheres belong to casters that
// the light's cascade set also draws: they count by beyondMaps, the weight of the far shadow where the maps end
// (beyondShadowMaps). Eclipse.Visibility is the same test on the CPU. L is the unit direction toward the light.
float farShadowVisibility(vec3 position, vec3 L, float angularRadius, uvec3 range, float beyondMaps)
{
    float lightRadius = max(angularRadius, 1e-5);
    float visibility = 1.0;
    for (uint i = 0u; i < range.y; i++)
    {
        float weight = i < range.z ? beyondMaps : 1.0;
        if (weight <= 0.0)
            continue;
        vec4 occluder = occluders[range.x + i];
        vec3 toCenter = occluder.xyz - position;
        float distanceToCenter = length(toCenter);
        if (distanceToCenter <= occluder.w)
            continue;
        // The test above makes the ratio below 1 only in exact arithmetic: a division may be off by 2.5 ULP, and for a
        // distance within 2 ULP of the radius, such as on a planet's own ground, the ratio can come out above 1.
        float occluderRadius = asin(min(occluder.w / distanceToCenter, 1.0));
        // atan2 of |cross| and dot keeps precision at small angles, where acos(dot) does not.
        float separation = atan(length(cross(toCenter, L)), dot(toCenter, L));
        visibility *= 1.0 - weight * discCoverage(lightRadius, occluderRadius, separation);
    }
    return visibility;
}

#ifndef TERTIUS_PROBE_TRUST
#define TERTIUS_PROBE_TRUST 1
#endif

// PROBE t1-af5w part 0 (scratch, never merged): the side in m of the square with the emitter's projected area, as a
// caster in the direction -L from the light sees it. A rect: sqrt(area x |cos|) about its axis. A tube: its projected
// capsule. A point or a spot: 2 R.
float probeEmitterSide(GpuLight light, vec3 L)
{
    uint kind = uint(light.intensityKind.w + 0.5);
    float r = light.spotRadius.y;
    if (kind == 3u)
    {
        vec3 a = light.directionCosOuter.xyz, b = light.halfHeight.xyz;
        vec3 n = cross(a, b);
        return sqrt(4.0 * abs(dot(n, L)));
    }
    if (kind == 2u)
    {
        vec3 a = light.directionCosOuter.xyz;
        float across = length(cross(a, L));
        return sqrt(4.0 * r * across + 3.14159265 * r * r);
    }
    return 2.0 * r;
}

// PROBE t1-af5w: the true width and the gap in texels at the receiver, from a caster of reversed depth boundsY.
vec2 probeTrueAndGap(GpuLight light, GpuShadowTile tile, float ndcZ, float boundsY, vec3 L, float d)
{
    float caster = d * ndcZ / max(boundsY, 1e-30);
    float gapTexels = (d - caster) * max(ndcZ, 1e-30) / (tile.texelPerMeter * tile.near);
    return vec2(gapTexels * probeEmitterSide(light, L) / max(caster, 1e-3), gapTexels);
}

// PROBE t1-af5w part 0: the footprint's width in tile texels, from the least ramp `least` to 4. The bounds texel is
// fetched again after the taps (tileLookupOf), so nothing new is held across them. The gap is from bounds.y, the
// window's caster nearest the light. The trusted gap is the gap minus the own-plane span (3 texels x tan θ) minus the
// depth bias (0.5 texels constant + 2.5 texels x tan θ slope). w is 4 where the trusted gap is 0 or less, and it fades to
// the true width over one own-plane span.
float probeFootprintTexels(GpuLight light, GpuShadowTile tile, float ndcZ, float boundsY, vec3 L, float d,
    float sinTheta, float least)
{
    // In texels at the receiver: the gap, and the true width.
    float caster = d * ndcZ / max(boundsY, 1e-30);
    float gapTexels = (d - caster) * max(ndcZ, 1e-30) / (tile.texelPerMeter * tile.near);
    float trueTexels = gapTexels * probeEmitterSide(light, L) / max(caster, 1e-3);
    // The trust rule (stills build: on when the least ramp is given positive, off when negative): w = 4 where the
    // trusted gap (the gap less the own-plane span of 3 tan θ texels and the bias of 0.5 + 2.5 tan θ texels) is 0 or
    // less, falling linearly to 0 over one own-plane span; the true width where it is larger. As one line in the gap:
    // 4 - 4 (gap - 0.5 - 5.5 tan θ) / (3 tan θ), with tan θ at least 1/12.
    float tanTheta = max(sinTheta * inversesqrt(max(1.0 - sinTheta * sinTheta, 1e-6)), 1.0 / 12.0);
    float untrusted = least > 0.0 ? 4.0 - (gapTexels - 0.5 - 5.5 * tanTheta) * (4.0 / 3.0) / tanTheta : 0.0;
    return clamp(max(trueTexels, untrusted), abs(least), 4.0);
}

// Where a lookup of a near light's atlas tile falls (tileLookupOf): its reversed depth, its tile's layer, the filter's first
// texel and its place within it, and the bounds of the window that holds the filter's 5 × 5 texels.
struct TileLookup
{
    vec3 ndc;
    float layer;
    vec2 first;
    vec2 f;
    vec2 bounds;   // the least (x) and the greatest (y) reversed depth of the window (shadow_bounds.comp)
    float rampScale; // PROBE t1-af5w part 0: 4 / w, the remap's slope; 1 keeps main
};

// The lookup of tileLitFraction, with the normal offset at the receiver's distance and the casters' slope-scaled bias.
TileLookup tileLookupOf(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta)
{
    // A point light's six views follow the cube faces in layer order (ShadowTiles.AddViews).
    uint view = light.shadow.y + (light.shadow.z > 1u ? cubeFaceOf(-L) : 0u);
    GpuShadowTile tile = shadowTiles[view];
    vec3 lookup = position + geometricNormal * (frame.shadowParams.y * tile.texelPerMeter * d * sinTheta);
    vec4 clip = tile.viewProj * vec4(lookup, 1.0);
    // The near plane floors the divisor, which is the lookup's depth along the view axis: a lookup nearer the light than
    // the near plane gets the depth 1, nearest the light, which no clamped caster passes, so the floor changes no result.
    TileLookup t;
    t.ndc = clip.xyz / max(clip.w, tile.near);
    vec2 uv = t.ndc.xy * 0.5 + 0.5;
    float size = float(textureSize(shadowAtlas, 0).x);
    t.layer = float(tile.layer);

    // The filter's 4 × 4 bilinear taps at −1.5 to 1.5 texels, computed exactly: it weighs the 5 × 5 texels from texel `first` by (1 − f, 1, 1, 1, f) along each axis, f the lookup's place within its texel. The hardware's
    // filter at the lookup's own place rounds f to the device's sub-texel precision, which Vulkan lets be as coarse as
    // 4 bits, so tileLitFraction applies f itself.
    vec2 corner = uv * size - 2.0;
    t.first = floor(corner);
    t.f = corner - t.first;

    // The bounds texel at floor(first / 2) holds the least (x) and the greatest (y) reversed depth of texels 2k to 2k + 5,
    // which hold the 5 × 5 (shadow_bounds.comp). Below the least, every one of them lies nearer the light than the
    // lookup, every comparison fails and the filter gives 0; at the greatest or above, none does, every one passes and it
    // gives 1. So the bounds decide those pixels with the filter's own result. The clamp comes before the conversion to
    // int, which is undefined for a float out of range; the filter's texels clamp to the tile's edge as the bounds' do.
    vec2 lastWindow = vec2(textureSize(shadowBounds, 0).yy - 1); // PROBE part 1: level 0 is the left square
    ivec2 window = ivec2(clamp(floor(corner * 0.5), vec2(0.0), lastWindow));
    t.bounds = texelFetch(shadowBounds, ivec3(window, int(tile.layer)), 0).rg;
    float least = frame.shadowParams.w;
    t.rampScale = least != 0.0 ? 4.0 / probeFootprintTexels(light, tile, t.ndc.z, t.bounds.y, L, d, sinTheta, least) : 1.0;
    return t;
}

// Whether the lookup's bounds show every tap of the filter shadowed, where tileLitFraction returns 0 exactly.
bool fullyShadowed(TileLookup t)
{
    return t.ndc.z < t.bounds.x;
}

// The lit fraction of a near light's shadow filter at a world position (docs/plans/local-light-shadows.md, "Sampling"):
// the one tile of a spot light, or the tile of the cube face of a point light that holds the direction from the light to
// the position. The filter's FilterTapsPerAxis × FilterTapsPerAxis PCF taps (the reads below are written for 4), computed
// with exact weights, with the normal offset at the receiver's distance and the casters' slope-scaled bias, as the
// cascades take them. Texels past the tile clamp to its edge. The tile's bounds skip the reads where every comparison
// agrees. L is the unit direction toward the light and d its distance; geometricNormal and sinTheta are as in
// cascadeShadow.
//
// Two ways to read the 5 × 5 texels give the same sum, and tileCornerMeans picks one for the device's driver
// (VulkanRenderer.HeldValuesCost). Nine gathers are the fewest reads. Each returns four comparisons, so the shader holds
// 36 values until the weights reduce them. Sixteen corner means are more reads of one value each. A driver that prices
// each read runs the gathers faster, and one that prices the held values runs the means faster (t2-gfq5).
float tileLitFractionOf(TileLookup t);
float tileLitFraction(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta)
{
    return tileLitFractionOf(tileLookupOf(light, position, L, d, geometricNormal, sinTheta));
}
// PROBE t1-af5w: today's filter from a given lookup.
float tileLitFractionOf(TileLookup t)
{
    vec3 ndc = t.ndc;
    vec2 first = t.first;
    vec2 f = t.f;
    float size = float(textureSize(shadowAtlas, 0).x);
    float layer = t.layer;
    if (fullyShadowed(t))
        return 0.0;
    if (ndc.z >= t.bounds.y)
        return 1.0;

    if (tileCornerMeans)
    {
        // The weights (1 − f, 1, 1, 1, f) of an axis are 1 − f times the four texels from texel 0, plus f times the four
        // texels from texel 1. So the filter is the blend of four sums of 4 × 4 texels by f, and each sum is four means
        // of 2 × 2 texels. A bilinear comparison at the shared corner of four texels is their mean at any sub-texel
        // precision, because each of its weights is a half. The corner x of an axis lies between the texels x and x + 1:
        // the even corners make the sum from texel 0, and the odd corners the sum from texel 1.
        float means = 0.0;
        for (int y = 0; y < 4; y++)
        {
            float wy = (y & 1) == 0 ? 1.0 - f.y : f.y;
            for (int x = 0; x < 4; x++)
            {
                float wx = (x & 1) == 0 ? 1.0 - f.x : f.x;
                vec2 at = (first + vec2(float(x) + 1.0, float(y) + 1.0)) / size;
                means += wx * wy * textureGrad(sampler2DArrayShadow(shadowAtlas, shadowSampler),
                    vec4(at, layer, ndc.z), vec2(0.0), vec2(0.0));
            }
        }
        // Each mean stands for four texels, and the weights of the 5 × 5 texels add up to 16.
        float litMeans = means * (4.0 / 16.0);
        if (false) // PROBE debug: w / 4 at the edge pixels, 0 elsewhere
            return litMeans > 0.0 && litMeans < 1.0 ? 1.0 / t.rampScale : 0.0;
        return clamp(0.5 + (litMeans - 0.5) * t.rampScale, 0.0, 1.0);
    }

    // Nine gathers of 2 × 2 comparisons read the 5 × 5 texels and one unweighted column and row beyond them.
    float wx[6] = float[6](1.0 - f.x, 1.0, 1.0, 1.0, f.x, 0.0);
    float wy[6] = float[6](1.0 - f.y, 1.0, 1.0, 1.0, f.y, 0.0);
    float lit = 0.0;
    for (int j = 0; j < 3; j++)
    {
        float upper = 0.0, lower = 0.0;
        for (int i = 0; i < 3; i++)
        {
            // A gather at the shared corner of four texels compares exactly those four: w the upper left, z the upper
            // right, x the lower left and y the lower right, with the upper row the one of the smaller index.
            vec2 at = (first + vec2(float(2 * i) + 1.0, float(2 * j) + 1.0)) / size;
            vec4 g = textureGather(sampler2DArrayShadow(shadowAtlas, shadowSampler), vec3(at, layer), ndc.z);
            upper += wx[2 * i] * g.w + wx[2 * i + 1] * g.z;
            lower += wx[2 * i] * g.x + wx[2 * i + 1] * g.y;
        }
        lit += wy[2 * j] * upper + wy[2 * j + 1] * lower;
    }
    lit *= 1.0 / 16.0;
    if (false) // PROBE debug: w / 4 at the edge pixels, 0 elsewhere
        return lit > 0.0 && lit < 1.0 ? 1.0 / t.rampScale : 0.0;
    return clamp(0.5 + (lit - 0.5) * t.rampScale, 0.0, 1.0);
}

// The share of a near light that its atlas tiles let through at a world position, with the arguments of
// tileLitFraction. The tiles keep the symmetric filter: the share is the lit fraction, so half the blur lies inside the
// shadow. lightOutsideShadow on the tiles was tried and refused (t1-gout): it made every lamp's shadow sharper and
// darker, which the user does not accept, and raised the floor's acne by 8/3. The tiles' leak under a caster's edge is
// small: about 6 pixels down a beam's face below a ceiling lamp of the exit cabin (t1-hdtm).
// PROBE t1-af5w part 1 (scratch): design 1, the pyramid. The bounds image holds level 0 (the left square, B x B) and
// levels 1 to 4 packed to its right (shadow_bounds.comp): rg the least and the greatest reversed depth, ba the moments.
ivec2 probeLevelOrigin(int level, int side)
{
    return level == 0 ? ivec2(0) : ivec2(side, side - ((2 * side) >> level));
}

// The greatest reversed depth (the caster nearest the light) of the 2 x 2 texels of `level` around tile place uv: a
// square of 2^(level + 2) tile texels.
float probeNearest(int level, vec2 uv, int layer)
{
    int side = textureSize(shadowBounds, 0).y;
    int n = side >> level;
    ivec2 origin = probeLevelOrigin(level, side);
    ivec2 at = ivec2(floor(uv * float(n) - 0.5));
    float nearest = 0.0;
    for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            nearest = max(nearest, texelFetch(shadowBounds,
                ivec3(origin + clamp(at + ivec2(x, y), ivec2(0), ivec2(n - 1)), layer), 0).g);
    return nearest;
}

// The moments of `level` at tile place uv, filtered bilinearly inside the level.
vec2 probeMomentsAt(int level, vec2 uv, float layer)
{
    ivec2 size = textureSize(shadowBounds, 0).xy;
    float n = float(size.y >> level);
    vec2 p = vec2(probeLevelOrigin(level, size.y)) + clamp(uv * n, vec2(0.5), vec2(n - 0.5));
    return textureGrad(sampler2DArray(shadowBounds, materialSampler), vec3(p / vec2(size), layer), vec2(0.0), vec2(0.0)).ba;
}

// The width w in tile texels of a lookup's footprint from a caster of reversed depth `caster`: the true width, but 4 or
// more where the caster's gap is not trusted (the narrow half's rule, trust on).
float probeWideTexels(GpuLight light, GpuShadowTile tile, TileLookup t, float caster, vec3 L, float d, float sinTheta)
{
    // The trust rule gives at most 4 (part 0), and widths up to 4 take today's filter, so only the true width decides
    // the wide path. (Before 13:30 this returned the untrusted term unclamped, which passed 4 at gaps under
    // 0.5 + 5.5 tan θ texels and sent contact pixels to the wide path.)
    return probeTrueAndGap(light, tile, t.ndc.z, caster, L, d).x;
}

// The greatest reversed depth of `level` at tile place uv, filtered bilinearly inside the level: a smooth estimate of the
// nearest caster, not a bound (width form 1).
float probeNearestSmooth(int level, vec2 uv, float layer)
{
    ivec2 size = textureSize(shadowBounds, 0).xy;
    float n = float(size.y >> level);
    vec2 p = vec2(probeLevelOrigin(level, size.y)) + clamp(uv * n, vec2(0.5), vec2(n - 0.5));
    return textureGrad(sampler2DArray(shadowBounds, materialSampler), vec3(p / vec2(size), layer), vec2(0.0), vec2(0.0)).g;
}

// The least reversed depth (the texel farthest from the light) of the 2 x 2 texels of `level` around tile place uv.
float probeLeast(int level, vec2 uv, int layer)
{
    int side = textureSize(shadowBounds, 0).y;
    int n = side >> level;
    ivec2 origin = probeLevelOrigin(level, side);
    ivec2 at = ivec2(floor(uv * float(n) - 0.5));
    float least = 1.0;
    for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            least = min(least, texelFetch(shadowBounds,
                ivec3(origin + clamp(at + ivec2(x, y), ivec2(0), ivec2(n - 1)), layer), 0).r);
    return least;
}

// Cap 8: the lit share of a box of w texels, 4 to 8, by 64 corner means with the sliding weights (1 - f, 1, ..., 1, f)
// over 9 x 9 texels, the form of tileLitFraction's sixteen corner means. The nested box of 4 texels (corners 2 to 5,
// today's filter) comes from the same reads, and the share is the blend of the two boxes by (w - 4) / 4, so it is
// today's share at w = 4.
float probeCap8(TileLookup t, float w)
{
    float size = float(textureSize(shadowAtlas, 0).x);
    vec2 corner = (t.ndc.xy * 0.5 + 0.5) * size - 4.0;
    vec2 first = floor(corner);
    vec2 f = corner - first;
    float box8 = 0.0, box4 = 0.0;
    for (int y = 0; y < 8; y++)
    {
        float wy = (y & 1) == 0 ? 1.0 - f.y : f.y;
        for (int x = 0; x < 8; x++)
        {
            float wx = (x & 1) == 0 ? 1.0 - f.x : f.x;
            vec2 at = (first + vec2(float(x) + 1.0, float(y) + 1.0)) / size;
            float m = wx * wy * textureGrad(sampler2DArrayShadow(shadowAtlas, shadowSampler),
                vec4(at, t.layer, t.ndc.z), vec2(0.0), vec2(0.0));
            box8 += m;
            if (x >= 2 && x <= 5 && y >= 2 && y <= 5)
                box4 += m;
        }
    }
    return mix(box4 * (4.0 / 16.0), box8 * (4.0 / 64.0), clamp((w - 4.0) / 4.0, 0.0, 1.0));
}

// Cap 8 by 25 gathers of 2 x 2 comparisons over 10 x 10 texels from the cap's first texel, with the weights applied by
// hand, the form of tileLitFraction's nine gathers: the 8-box weighs texels 0 to 8 by (1 - f, 1, ..., 1, f), and the
// nested 4-box weighs texels 2 to 6 by (1 - f, 1, 1, 1, f).
float probeCap8Gathers(TileLookup t, float w, float w0)
{
    float size = float(textureSize(shadowAtlas, 0).x);
    vec2 corner = (t.ndc.xy * 0.5 + 0.5) * size - 4.0;
    vec2 first = floor(corner);
    vec2 f = corner - first;
    float box8 = 0.0, box4 = 0.0;
    for (int j = 0; j < 5; j++)
    {
        float up8 = 0.0, low8 = 0.0, up4 = 0.0, low4 = 0.0;
        for (int i = 0; i < 5; i++)
        {
            vec2 at = (first + vec2(float(2 * i) + 1.0, float(2 * j) + 1.0)) / size;
            vec4 g = textureGather(sampler2DArrayShadow(shadowAtlas, shadowSampler), vec3(at, t.layer), t.ndc.z);
            int a = 2 * i, b = 2 * i + 1;
            float a8 = a == 0 ? 1.0 - f.x : 1.0;
            float b8 = b == 9 ? 0.0 : 1.0;
            float a4 = a == 2 ? 1.0 - f.x : a == 4 ? 1.0 : a == 6 ? f.x : 0.0;
            float b4 = b == 3 || b == 5 ? 1.0 : 0.0;
            if (a == 8) a8 = f.x;
            up8 += a8 * g.w + b8 * g.z;
            low8 += a8 * g.x + b8 * g.y;
            up4 += a4 * g.w + b4 * g.z;
            low4 += a4 * g.x + b4 * g.y;
        }
        int a = 2 * j, b = 2 * j + 1;
        float a8 = a == 0 ? 1.0 - f.y : a == 8 ? f.y : 1.0;
        float b8 = b == 9 ? 0.0 : 1.0;
        float a4 = a == 2 ? 1.0 - f.y : a == 4 ? 1.0 : a == 6 ? f.y : 0.0;
        float b4 = b == 3 || b == 5 ? 1.0 : 0.0;
        box8 += a8 * up8 + b8 * low8;
        box4 += a4 * up4 + b4 * low4;
    }
    return mix(box4 * (1.0 / 16.0), box8 * (1.0 / 64.0), clamp((w - w0) / (8.0 - w0), 0.0, 1.0));
}

// The least (x) and the greatest (y) reversed depth of the 10 x 10 tile texels from the lookup's cap-8 first texel: four
// level-0 windows (6 x 6 texels each) at a stride of 2 windows, which hold the cap's 9 x 9 texels.
vec2 probeCap8Bounds(TileLookup t)
{
    float size = float(textureSize(shadowAtlas, 0).x);
    int last = textureSize(shadowBounds, 0).y - 1;
    ivec2 k = ivec2(floor(((t.ndc.xy * 0.5 + 0.5) * size - 4.0) * 0.5));
    vec2 bounds = vec2(1.0, 0.0);
    for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
        {
            vec2 b = texelFetch(shadowBounds, ivec3(clamp(k + 2 * ivec2(x, y), ivec2(0), ivec2(last)), int(t.layer)), 0).rg;
            bounds = vec2(min(bounds.x, b.x), max(bounds.y, b.y));
        }
    return bounds;
}

// The plain moments' lit share of a receiver at reversed depth r, with the moments m.
float probeChebyshev(vec2 m, float r)
{
    float variance = max(m.y - m.x * m.x, 1e-6 * r * r);
    return r >= m.x ? 1.0 : variance / (variance + (m.x - r) * (m.x - r));
}

// frame.renderParams.w = 1000 form + 100 debug + mode. form: 0 the nearest caster by two fetches of the least-and-greatest
// levels, 1 the same filtered bilinearly, 2 the blockers' mean from the moments at the 64-texel search footprint
// (variance soft shadows: (m - p r) / (1 - p), plain moments only). debug: 0 none, 1 the bleed against an 8 x 8 PCF of
// the same footprint times 8, 2 that PCF in place of the moments, 3 w / 64, 4 one. mode: integer part 1 plain moments,
// c >= 2 exponential moments of exp(c z); fraction the cut b.
float probePyramidShare(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta,
    out bool decided)
{
    float v = frame.renderParams.w;
    float form = floor(v / 1000.0);
    v -= 1000.0 * form;
    float debug = floor(v / 100.0);
    float mode = v - 100.0 * debug;
    float outside = debug == 1.0 || debug == 3.0 || debug == 4.0 || debug == 5.0 ? 0.0 : 1.0;
    TileLookup t = tileLookupOf(light, position, L, d, geometricNormal, sinTheta);
    uint view = light.shadow.y + (light.shadow.z > 1u ? cubeFaceOf(-L) : 0u);
    GpuShadowTile tile = shadowTiles[view];
    vec2 uv = t.ndc.xy * 0.5 + 0.5;
    int layer = int(t.layer);
    decided = true;
    float r = t.ndc.z;
    if (floor(mode) == 90.0 || floor(mode) == 92.0 || floor(mode) == 93.0)
    {
        // 14:25: modes 92 and 93 carry the entry width w0 = 4 + 10 x fract(mode); the blend from the box of 4 to the box
        // of 8 runs over w0 to 8. Mode 93 replaces the reads by one value (the floor of the wide half's cost).
        bool gathers = floor(mode) != 90.0;
        float w0 = gathers ? 4.0 + floor(fract(mode) * 10.0 + 0.5) : 4.0;
        // Cap 8 with the cuts of 13:23. (c) The width first, from one bilinear read of level 1's nearest caster (8 tile
        // texels per window), and today's filter where it is 4 texels or less. (a) Then the exact bounds of the cap's own
        // 9 x 9 texels skip the reads where every texel agrees.
        float w = probeTrueAndGap(light, tile, r, max(probeNearestSmooth(1, uv, t.layer), r), L, d).x;
        if (w <= w0)
        {
            decided = false;
            return 0.0;
        }
        vec2 bounds = probeCap8Bounds(t);
        if (r >= bounds.y)
            return outside;
        if (r < bounds.x)
            return 0.0;
        if (debug == 3.0)
            return min(w, 8.0) / 64.0;
        if (debug == 5.0)
        {
            // The pixels that take the reads; fract(mode) x 10 - 1 picks one light kind (0 point, 1 spot, 2 tube, 3
            // rect), and 0 takes every kind.
            float pick = gathers ? 0.0 : floor(fract(mode) * 10.0 + 0.5);
            // 5 the rects over 0.5 m² (the 1.2 m panels), 6 the rects up to 0.5 m² (the 0.15 x 0.5 m panels).
            float area = 4.0 * length(cross(light.directionCosOuter.xyz, light.halfHeight.xyz));
            if (pick >= 5.0)
                return light.intensityKind.w == RectKind && (pick == 5.0) == (area > 0.5) ? 1.0 : 0.0;
            return pick == 0.0 || abs(light.intensityKind.w - (pick - 1.0)) < 0.5 ? 1.0 : 0.0;
        }
        if (floor(mode) == 93.0)
            return clamp((w - w0) / (8.0 - w0), 0.0, 1.0) * 0.5 + 0.25;
        return gathers ? probeCap8Gathers(t, w, w0) : probeCap8(t, w);
    }
    float caster;
    if (form == 2.0)
    {
        // The blockers' mean depth from the moments of the 64-texel search footprint (level 4).
        vec2 m = probeMomentsAt(4, uv, t.layer);
        float p = probeChebyshev(m, r);
        if (p >= 0.999)
            return outside;
        caster = (m.x - p * r) / (1.0 - p);
    }
    else
    {
        // The skip uses the exact bound; form 1 takes the width from the bilinear estimate.
        caster = probeNearest(4, uv, layer);
        if (r >= caster)
            return outside;
        if (form == 1.0)
            caster = max(probeNearestSmooth(4, uv, t.layer), r);
    }
    float w = min(probeWideTexels(light, tile, t, caster, L, d, sinTheta), 64.0);
    if (w <= 4.0)
    {
        decided = false;
        return 0.0;
    }
    if (form != 2.0)
    {
        // Once more within the footprint of that width.
        int refine = clamp(int(ceil(log2(w))) - 2, 0, 4);
        float nearer = probeNearest(refine, uv, layer);
        if (r >= nearer)
            return outside;
        if (form == 1.0)
            nearer = max(probeNearestSmooth(refine, uv, t.layer), r);
        w = probeWideTexels(light, tile, t, nearer, L, d, sinTheta);
    }
    w = clamp(w, 4.0, 64.0);
    if (floor(mode) == 90.0 || floor(mode) == 91.0)
    {
        // The taps: cap 8 (mode 90) or cap 16 (91). The exact bounds of level 2 (2 x 2 texels of 8 tile texels) skip
        // the reads where every texel of the footprint agrees.
        w = min(w, floor(mode) == 90.0 ? 8.0 : 16.0);
        if (r >= probeNearest(2, uv, layer))
            return outside;
        if (r < probeLeast(2, uv, layer))
            return debug == 5.0 ? 0.0 : 0.0;
        if (debug == 3.0)
            return w / 64.0;
        if (debug == 4.0 || debug == 5.0)
            return 1.0;
        if (floor(mode) == 90.0)
            return probeCap8(t, w);
        float size = float(textureSize(shadowAtlas, 0).x);
        float sum = 0.0;
        for (int y = 0; y < 8; y++)
            for (int x2 = 0; x2 < 8; x2++)
            {
                vec2 at = uv + (vec2(float(x2), float(y)) - 3.5) * (w / 8.0) / size;
                sum += texture(sampler2DArrayShadow(shadowAtlas, shadowSampler), vec4(at, t.layer, r));
            }
        return sum / 64.0;
    }
    if (debug == 3.0)
        return w / 64.0;
    if (debug == 4.0)
        return 1.0;
    float level = clamp(log2(w / 4.0), 0.0, 4.0);
    int below = int(floor(level));
    vec2 m = mix(probeMomentsAt(below, uv, t.layer), probeMomentsAt(min(below + 1, 4), uv, t.layer), level - float(below));
    float warp = floor(mode);
    float cut = fract(mode);
    float x = warp >= 2.0 ? exp(warp * r) : r;
    float least = warp >= 2.0 ? x * warp * 1e-3 * r : 1e-3 * r;
    float variance = max(m.y - m.x * m.x, least * least);
    float share = x >= m.x ? 1.0 : variance / (variance + (m.x - x) * (m.x - x));
    share = clamp((share - cut) / (1.0 - cut), 0.0, 1.0);
    if (debug == 1.0 || debug == 2.0)
    {
        float size = float(textureSize(shadowAtlas, 0).x);
        float reference = 0.0;
        for (int y = 0; y < 8; y++)
            for (int x2 = 0; x2 < 8; x2++)
            {
                vec2 at = uv + (vec2(float(x2), float(y)) - 3.5) * (w / 8.0) / size;
                reference += texture(sampler2DArrayShadow(shadowAtlas, shadowSampler), vec4(at, t.layer, r));
            }
        return debug == 2.0 ? reference / 64.0 : 8.0 * max(share - reference / 64.0, 0.0);
    }
    return share;
}

#ifndef TERTIUS_PROBE_SHARED_LOOKUP
#define TERTIUS_PROBE_SHARED_LOOKUP 1
#endif

// PROBE t1-af5w, 15:05: the lean wide half. One gather of level 1's nearest casters (G) rejects first: level-1 texels k
// and k + 1 hold tile texels 4k to 4k + 11, which hold the cap's 9 x 9 texels from floor(s - 4) for k = floor(that / 4).
// Where no caster is nearer the light than the receiver, the share is 1 with no width. Then the width, with the
// emitter's side as one value per light (sqrt of its area, no cosine toward the caster), and today's filter where it is
// 4 texels or less. Then one gather of the least depths (R) rejects the fully shadowed footprint. reads false replaces
// the 25 gathers by one value (the floor).
float probeLeanShare(GpuLight light, vec3 L, float d, TileLookup t, bool reads, out bool decided)
{
    decided = true;
    float r = t.ndc.z;
    ivec2 size = textureSize(shadowBounds, 0).xy;
    float n = float(size.y >> 1);
    float atlas = float(textureSize(shadowAtlas, 0).x);
    vec2 s = (t.ndc.xy * 0.5 + 0.5) * atlas;
    vec2 k = clamp(floor(floor(s - 4.0) * 0.25), vec2(0.0), vec2(n - 2.0));
    vec2 at = (vec2(float(size.y), 0.0) + k + 1.0) / vec2(size);
    vec4 nearest = textureGather(sampler2DArray(shadowBounds, materialSampler), vec3(at, t.layer), 1);
    if (r >= max(max(nearest.x, nearest.y), max(nearest.z, nearest.w)))
        return 1.0;
    uint view = light.shadow.y + (light.shadow.z > 1u ? cubeFaceOf(-L) : 0u);
    float texelAtNear = shadowTiles[view].texelPerMeter * shadowTiles[view].near;
    float side = light.intensityKind.w == RectKind
        ? sqrt(4.0 * length(cross(light.directionCosOuter.xyz, light.halfHeight.xyz)))
        : 2.0 * light.spotRadius.y;
    float caster = max(probeNearestSmooth(1, s / atlas, t.layer), r);
    float casterDistance = d * r / max(caster, 1e-30);
    float w = (d - casterDistance) * r / texelAtNear * side / max(casterDistance, 1e-3);
    if (w <= 4.0)
    {
        decided = false;
        return 0.0;
    }
    vec4 least = textureGather(sampler2DArray(shadowBounds, materialSampler), vec3(at, t.layer), 0);
    if (r < min(min(least.x, least.y), min(least.z, least.w)))
        return 0.0;
    if (!reads)
        return clamp((w - 4.0) / 4.0, 0.0, 1.0) * 0.5 + 0.25;
    return probeCap8Gathers(t, w, 4.0);
}

float tileShadow(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta)
{
    float leanMode = mod(frame.renderParams.w, 100.0);
    if (true)
    {
        // Mode 94 the lean floor, 95 the lean form with the 25 gathers at w0 = 4.
        bool decided;
#if TERTIUS_PROBE_SHARED_LOOKUP
        TileLookup t = tileLookupOf(light, position, L, d, geometricNormal, sinTheta);
        float share = probeLeanShare(light, L, d, t, true, decided);
        return decided ? share : tileLitFractionOf(t);
#else
        float share = probeLeanShare(light, L, d, tileLookupOf(light, position, L, d, geometricNormal, sinTheta),
            leanMode >= 95.0, decided);
        return decided ? share : tileLitFraction(light, position, L, d, geometricNormal, sinTheta);
#endif
    }
    if (frame.renderParams.w >= 1.0)
    {
        bool decided;
        float share = probePyramidShare(light, position, L, d, geometricNormal, sinTheta, decided);
        if (decided)
            return share;
        float debug = floor(mod(frame.renderParams.w, 1000.0) / 100.0);
        if (debug == 1.0 || debug == 3.0 || debug == 4.0 || debug == 5.0)
            return 0.0;
    }
    return tileLitFraction(light, position, L, d, geometricNormal, sinTheta);
}

#endif
