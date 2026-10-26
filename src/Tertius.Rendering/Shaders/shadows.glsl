// Cascaded sun shadow maps (ShadowCascades.cs, ShadowPass.cs). Needs frame.glsl and gbuffer_inputs.glsl.
//
// The main view's cascade selection is by view depth. Over the last 10% of each cascade the result blends into the next cascade,
// and the last cascade fades out to fully lit at 2 km, so there are no shadow-map shadows beyond it.
//
// Filtering: 4 × 4 hardware-PCF taps one texel apart. Each tap is a bilinear 2 × 2 comparison, so together they
// cover 5 × 5 texels with a smooth tent-like weight.
//
// Bias, in three parts that each scale with the cascade's texel size:
//  - The shadow pass adds a slope-scaled depth bias to the casters, which covers the difference between a tap's
//    position and the centers of the texels its bilinear footprint compares against.
//  - Receiver-plane depth: where the receiver's own plane rises toward the sun, a tap compares against the plane's
//    depth at the tap's position rather than the pixel's, so the wide kernel does not shadow a surface with itself.
//    The plane comes from the geometric normal; its slope is limited where the surface is almost parallel to the
//    light. The correction is one-sided: where the plane falls away from the sun, the pixel's own depth is kept,
//    because a surface farther from the sun than the receiver cannot shadow it. Extrapolating the plane downhill
//    would let the kernel find a wall standing on the receiver's plane and creep shadow onto its sunlit side.
//  - A normal offset of one texel, scaled by sin(angle to the light), plus half a texel of constant depth bias.
#ifndef TERTIUS_SHADOWS_GLSL
#define TERTIUS_SHADOWS_GLSL

// The lit fraction of one cascade at a world position.
float sampleCascade(uint cascade, vec3 position, vec3 geometricNormal, float NdotL)
{
    float texel = frame.shadowTexel[cascade];
    float sinTheta = sqrt(max(1.0 - NdotL * NdotL, 0.0));
    vec3 lookup = position + geometricNormal * (frame.shadowParams.y * texel * sinTheta);
    vec4 clip = frame.shadowViewProj[cascade] * vec4(lookup, 1.0);
    vec2 uv = clip.xy * 0.5 + 0.5;

    // The receiver's plane: depth changes by −n.xy / n.z per unit of lateral distance in light space, which is
    // shadowAxisX.w times that in [0, 1] depth per unit of uv.
    vec3 lightNormal = vec3(dot(geometricNormal, frame.shadowAxisX.xyz), dot(geometricNormal, frame.shadowAxisY.xyz), NdotL);
    vec2 slope = -lightNormal.xy / max(lightNormal.z, frame.shadowParams.w) * frame.shadowAxisX.w;
    float reference = clip.z + frame.shadowParams.z;
    float invSize = frame.shadowParams.x;

    float lit = 0.0;
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            vec2 offset = vec2(float(x) - 1.5, float(y) - 1.5) * invSize;
            vec4 coordinate = vec4(uv + offset, float(cascade), reference + max(dot(slope, offset), 0.0));
            // Explicit zero gradients (mip 0): the calling code branches per pixel, where implicit derivatives are undefined.
            lit += textureGrad(sampler2DArrayShadow(shadowCascades, shadowSampler), coordinate, vec2(0.0), vec2(0.0));
        }
    }
    return lit * (1.0 / 16.0);
}

// How far a world position is beyond the shadow maps: 0 where they shadow it, 1 past their end, and a smooth step over
// the last cascade's fade, where the maps' shadow fades out. A far shadow of a caster that the maps also draw counts by
// this weight, so the two shadows hand over without a seam.
float beyondShadowMaps(vec3 position)
{
    uint count = frame.counts.z;
    if (count == 0u)
        return 1.0;
    float depth = dot(position, frame.cameraForward.xyz);
    float start = frame.shadowBlendStart[count - 1u], end = frame.shadowSplits[count - 1u];
    return depth >= end ? 1.0 : smoothstep(0.0, 1.0, clamp((depth - start) / (end - start), 0.0, 1.0));
}

// The fraction of the sun that the shadow maps let through at a world position. geometricNormal is the
// interpolated vertex normal; NdotL is its cosine with the sun direction and must be positive.
float sunShadow(vec3 position, vec3 geometricNormal, float NdotL)
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

    float lit = sampleCascade(cascade, position, geometricNormal, NdotL);
    float blendStart = frame.shadowBlendStart[cascade];
    if (depth > blendStart)
    {
        float t = (depth - blendStart) / (frame.shadowSplits[cascade] - blendStart);
        float next = cascade + 1u < count ? sampleCascade(cascade + 1u, position, geometricNormal, NdotL) : 1.0;
        lit = mix(lit, next, smoothstep(0.0, 1.0, t));
    }
    return lit;
}

// The views that are not the main view, the environment faces, pick the finest cascade whose box holds the point
// (docs/plans/environment-map.md, "The sun's shadow maps in a face"). View depth along the camera's forward axis holds
// only for points in the main view's frustum: a point behind the camera has a negative depth, and cascade 0 does not hold
// it once it lies more than a few meters back.

// How far from a box's edge, in texels, a held point lies at least: the 4 × 4 taps reach 2 texels from the lookup point,
// and the normal offset moves the lookup up to 1 texel.
const float ShadowHoldingMarginTexels = 3.0;

// A position in the clip space of a cascade box: xy in [−1, 1] across the box, z in [0, 1] along the sun.
vec3 cascadeClip(uint cascade, vec3 position)
{
    return (frame.shadowViewProj[cascade] * vec4(position, 1.0)).xyz;
}

// How far across a cascade box a clip position lies: 0 at the box's center, 1 at the margin of the filter, more beyond.
float acrossBox(vec3 clip)
{
    float margin = 2.0 * ShadowHoldingMarginTexels * frame.shadowParams.x;
    return max(abs(clip.x), abs(clip.y)) / (1.0 - margin);
}

// Whether a cascade box holds a clip position with the margin of the filter.
bool boxHolds(vec3 clip)
{
    return acrossBox(clip) <= 1.0 && clip.z >= 0.0 && clip.z <= 1.0;
}

// The finest cascade whose box holds a world position, or frame.counts.z when none does.
uint holdingCascade(vec3 position)
{
    uint count = frame.counts.z;
    uint cascade = 0u;
    while (cascade < count && !boxHolds(cascadeClip(cascade, position)))
        cascade++;
    return cascade;
}

// How far a world position held by the coarsest box alone lies into that box's outer tenth: 0 inside it, to 1 at its edge.
float coarsestBoxFade(vec3 position)
{
    float across = acrossBox(cascadeClip(frame.counts.z - 1u, position));
    return smoothstep(0.0, 1.0, clamp((across - 0.9) / 0.1, 0.0, 1.0));
}

// How far a world position is beyond the shadow maps by the finest box that holds it: 0 in a box finer than the coarsest,
// to 1 over the coarsest box's outer tenth, and 1 outside every box. A far shadow of a caster that the maps also draw
// counts by this weight, as beyondShadowMaps does for the main view.
float beyondShadowMapsHolding(vec3 position)
{
    uint count = frame.counts.z;
    if (count == 0u)
        return 1.0;
    uint cascade = holdingCascade(position);
    if (cascade == count)
        return 1.0;
    return cascade + 1u == count ? coarsestBoxFade(position) : 0.0;
}

// The fraction of the sun that the shadow maps let through at a world position, from the finest cascade whose box holds
// it. In the coarsest box's outer tenth the map shadow fades out by the weight of beyondShadowMapsHolding. NdotL is the
// cosine of geometricNormal with the sun direction and must be positive.
float sunShadowHolding(vec3 position, vec3 geometricNormal, float NdotL)
{
    uint count = frame.counts.z;
    if (count == 0u)
        return 1.0;
    uint cascade = holdingCascade(position);
    if (cascade == count)
        return 1.0;
    float lit = sampleCascade(cascade, position, geometricNormal, NdotL);
    return cascade + 1u == count ? mix(lit, 1.0, coarsestBoxFade(position)) : lit;
}

#endif
