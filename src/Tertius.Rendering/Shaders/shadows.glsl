// Cascaded sun shadow maps (ShadowCascades.cs, ShadowPass.cs). Needs frame.glsl and gbuffer_inputs.glsl.
//
// Cascade selection is by view depth. Over the last 10% of each cascade the result blends into the next cascade,
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

#endif
