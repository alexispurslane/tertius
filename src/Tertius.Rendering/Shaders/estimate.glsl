// The estimate of the light around a point (docs/plans/screen-space-illumination.md, "The estimate of a texel"): the
// bounce G, pre-exposed, the occlusion V, and the blocked share of the map's light, 1 − S, in each channel. Term A, term
// B and the estimate target store it as seven half floats in an RGBA32UI texel, two to a word as packHalf2x16 packs
// them: x G.rg, y (G.b, V), z (1 − S).rg, w ((1 − S).b, 0). The images store 1 − S, not S, so that an open texel
// stores exact zeros, which the blurs and the upsample keep as zeros, and an open point keeps exactly D(n): the blurs'
// sums of ones can come to just below 1, and a device that rounds a store toward zero keeps that. AmbientEstimate in
// Shading/ is the C# copy, and it holds S.
#ifndef TERTIUS_ESTIMATE_GLSL
#define TERTIUS_ESTIMATE_GLSL

struct Estimate
{
    vec3 bounce;       // G, pre-exposed: the blockers' light, from 0 to c
    float occlusion;   // V: the open share of the cosine weight, from 0 to 1
    vec3 blockedShare; // 1 − S: the blocked directions' share of the map's light, from 0 to 1
};

// The estimate of a texel that nothing blocks, or that holds no information: G = 0, V = 1, S = 1.
Estimate openEstimate()
{
    return Estimate(vec3(0.0), 1.0, vec3(0.0));
}

uvec4 packEstimate(Estimate e)
{
    return uvec4(packHalf2x16(e.bounce.rg), packHalf2x16(vec2(e.bounce.b, e.occlusion)),
                 packHalf2x16(e.blockedShare.rg), packHalf2x16(vec2(e.blockedShare.b, 0.0)));
}

Estimate unpackEstimate(uvec4 packed)
{
    vec2 bounceBAndOcclusion = unpackHalf2x16(packed.y);
    return Estimate(vec3(unpackHalf2x16(packed.x), bounceBAndOcclusion.x), bounceBAndOcclusion.y,
                    vec3(unpackHalf2x16(packed.z), unpackHalf2x16(packed.w).x));
}

// sum + weight · e, channel by channel: the step of a weighted mean of estimates, which the denoise and the upsample take.
Estimate addWeighted(Estimate sum, Estimate e, float weight)
{
    return Estimate(sum.bounce + weight * e.bounce, sum.occlusion + weight * e.occlusion,
                    sum.blockedShare + weight * e.blockedShare);
}

// The estimate over a positive total weight: the end of a weighted mean.
Estimate dividedEstimate(Estimate sum, float totalWeight)
{
    return Estimate(sum.bounce / totalWeight, sum.occlusion / totalWeight, sum.blockedShare / totalWeight);
}

#endif
