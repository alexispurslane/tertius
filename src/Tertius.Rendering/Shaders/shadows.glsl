// The shadows of the lights (docs/plans/local-light-shadows.md): the cascade sets of the far lights (ShadowCascades.cs,
// ShadowPass.cs), the far shadows of each far light, and the tiles of the near lights (ShadowTiles.cs, LocalShadowPass.cs).
// Needs frame.glsl and gbuffer_inputs.glsl.
//
// The main view's cascade selection is by view depth. Over the last 10% of each cascade the result blends into the next cascade,
// and the last cascade fades out to fully lit at 2 km, so there are no shadow-map shadows beyond it.
//
// Filtering: 4 × 4 hardware-PCF taps one texel apart. Each tap is a bilinear 2 × 2 comparison, so together they
// cover 5 × 5 texels with a smooth tent-like weight. The tiles compute the same filter with exact weights, from nine
// gathers or from sixteen reads at texel corners (tileShadow).
//
// Bias, the same model for the cascades and the tiles (docs/plans/local-light-shadows.md, decision 33), in parts that
// each scale with the texel size:
//  - The shadow passes add a slope-scaled depth bias to the casters, ShadowBias.SlopeBias. Every tap compares against
//    the receiver's own depth, and the filter compares texels up to 2.5 texels from the lookup along each texel axis,
//    so the bias covers the receiver's slope over that reach along an axis. On a slope diagonal to the texel grid the
//    corner texels lie up to 2.5 texels off along both axes, and Vulkan lets the slope be the larger of the two axes'
//    slopes, so their comparisons can fall a little short: at most about 2% of the light at extreme grazing angles
//    (ARCHITECTURE.md, "Shadow bias"). No tap compares against the receiver's plane extrapolated toward the light: at a
//    grazing light, that plane passes under a wall that stands on the receiver, and the taps saw the light past the
//    wall (ter-kn1d).
//  - A normal offset of one texel, scaled by sin(angle to the light), frame.shadowParams.y, which the batcher writes in
//    every frame for the cascades and the tiles alike, plus half a texel of constant depth bias for the cascades.
#ifndef TERTIUS_SHADOWS_GLSL
#define TERTIUS_SHADOWS_GLSL

#include "cube_faces.glsl"

// ShaderConstants.TileCornerMeans: whether tileShadow reads a tile's texels as sixteen corner means, or as nine gathers.
layout(constant_id = 11) const bool tileCornerMeans = false;

// The lit fraction of one cascade of one cascade set at a world position, with geometricNormal and sinTheta as in
// cascadeShadow.
float sampleCascade(uint set, uint cascade, vec3 position, vec3 geometricNormal, float sinTheta)
{
    float texel = frame.shadowTexel[cascade];
    vec3 lookup = position + geometricNormal * (frame.shadowParams.y * texel * sinTheta);
    vec4 clip = frame.cascadeSets[set].viewProj[cascade] * vec4(lookup, 1.0);
    vec2 uv = clip.xy * 0.5 + 0.5;
    float reference = clip.z + frame.shadowParams.z;
    float invSize = frame.shadowParams.x;
    // ShadowMap.Layer: the layers of a set follow those of the sets before it.
    float layer = float(set * 4u + cascade);

    float lit = 0.0;
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            vec2 offset = vec2(float(x) - 1.5, float(y) - 1.5) * invSize;
            vec4 coordinate = vec4(uv + offset, layer, reference);
            // Explicit zero gradients (mip 0): the calling code branches per pixel, where implicit derivatives are undefined.
            lit += textureGrad(sampler2DArrayShadow(shadowCascades, shadowSampler), coordinate, vec2(0.0), vec2(0.0));
        }
    }
    return lit * (1.0 / 16.0);
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

// How far the 4 × 4 taps reach from the lookup, in texels: they compare the 5 × 5 texels around the lookup's texel, up
// to 2 texels from it. The C# twin is ShadowBias.FilterClearanceTexels.
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

// Where a lookup of a near light's atlas tile falls (tileLookupOf): its reversed depth, its tile's layer, the filter's first
// texel and its place within it, and the bounds of the window that holds the filter's 5 × 5 texels.
struct TileLookup
{
    vec3 ndc;
    float layer;
    vec2 first;
    vec2 f;
    vec2 bounds;   // the least (x) and the greatest (y) reversed depth of the window (shadow_bounds.comp)
};

// The lookup of tileShadow, with the normal offset at the receiver's distance and the casters' slope-scaled bias.
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

    // The filter of the cascades' 4 × 4 bilinear taps at −1.5 to 1.5 texels, computed exactly: it weighs the 5 × 5 texels
    // from texel `first` by (1 − f, 1, 1, 1, f) along each axis, f the lookup's place within its texel. The hardware's
    // filter at the lookup's own place rounds f to the device's sub-texel precision, which Vulkan lets be as coarse as
    // 4 bits, so tileShadow applies f itself.
    vec2 corner = uv * size - 2.0;
    t.first = floor(corner);
    t.f = corner - t.first;

    // The bounds texel at floor(first / 2) holds the least (x) and the greatest (y) reversed depth of texels 2k to 2k + 5,
    // which hold the 5 × 5 (shadow_bounds.comp). Below the least, every one of them lies nearer the light than the
    // lookup, every comparison fails and the filter gives 0; at the greatest or above, none does, every one passes and it
    // gives 1. So the bounds decide those pixels with the filter's own result. The clamp comes before the conversion to
    // int, which is undefined for a float out of range; the filter's texels clamp to the tile's edge as the bounds' do.
    vec2 lastWindow = vec2(textureSize(shadowBounds, 0).xy - 1);
    ivec2 window = ivec2(clamp(floor(corner * 0.5), vec2(0.0), lastWindow));
    t.bounds = texelFetch(shadowBounds, ivec3(window, int(tile.layer)), 0).rg;
    return t;
}

// Whether the lookup's bounds show every tap of the filter shadowed, where tileShadow returns 0 exactly.
bool fullyShadowed(TileLookup t)
{
    return t.ndc.z < t.bounds.x;
}

// The share of a near light that its atlas tiles let through at a world position (docs/plans/local-light-shadows.md,
// "Sampling"): the one tile of a spot light, or the tile of the cube face of a point light that holds the direction from
// the light to the position. The filter of the cascades' 4 × 4 PCF taps, computed with exact weights, with the normal
// offset at the receiver's distance and the casters' slope-scaled bias, as the cascades take them. Texels past the tile
// clamp to its edge. The tile's bounds skip the reads where every comparison agrees. L is the unit direction toward the
// light and d its distance; geometricNormal and sinTheta are as in cascadeShadow.
//
// Two ways to read the 5 × 5 texels give the same sum, and tileCornerMeans picks one for the device's driver
// (VulkanRenderer.HeldValuesCost). Nine gathers are the fewest reads. Each returns four comparisons, so the shader holds
// 36 values until the weights reduce them. Sixteen corner means are more reads of one value each. A driver that prices
// each read runs the gathers faster, and one that prices the held values runs the means faster (t2-gfq5).
float tileShadow(GpuLight light, vec3 position, vec3 L, float d, vec3 geometricNormal, float sinTheta)
{
    TileLookup t = tileLookupOf(light, position, L, d, geometricNormal, sinTheta);
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
        return means * (4.0 / 16.0);
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
    return lit * (1.0 / 16.0);
}

#endif
