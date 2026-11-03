#version 460
// "Light candidates" (docs/plans/temporal-renderer.md, section 7, "The two passes"): one invocation per pixel draws the
// candidates from the light table, reuses the last frame's reservoir at the pixel's previous place, tests the chosen
// light's visibility and writes R0. It reads R1, which the last frame's direct pass wrote.
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "restir.glsl"
#include "reservoirs.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out uvec2 outReservoir;

// The cap of M of the last frame's reservoir: 20 times the candidates.
const float HistoryCandidates = 20.0;

// The pass's constant in the random numbers.
const uint CandidatesStream = 0x1u;

// The texel of the last frame's image that holds the most bilinear weight among the 2 × 2 that pass the surface test
// around the pixel's previous place, or (−1, −1) where none passes.
ivec2 previousTexel(ivec2 pixel)
{
    uint bits = imageLoad(surfaceTest, pixel).r & 0xFu;
    if (bits == 0u)
        return ivec2(-1);
    vec2 place = (vec2(pixel) + 0.5) * frame.viewport.zw + imageLoad(motionTarget, pixel).xy;
    vec2 texelPlace = place * frame.previousViewport.xy - 0.5;
    ivec2 first = ivec2(floor(texelPlace));
    vec2 f = texelPlace - vec2(first);
    ivec2 best = ivec2(-1);
    float most = -1.0;
    for (uint tap = 0u; tap < 4u; tap++)
    {
        if ((bits & (1u << tap)) == 0u)
            continue;
        ivec2 offset = ivec2(tap & 1u, tap >> 1);
        vec2 axes = mix(1.0 - f, f, vec2(offset));
        if (axes.x * axes.y > most)
        {
            most = axes.x * axes.y;
            best = first + offset;
        }
    }
    return best;
}

void main()
{
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), textureSize(gbufferDistance, 0) - 1);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    uint sampled = sampledLights();
    // Sky and impostors take no light.
    if (distanceToEye <= 0.0 || texelFetch(gbufferBaseColor, pixel, 0).a == 0.0 || sampled == 0u)
    {
        outReservoir = packReservoir(emptyReservoir());
        return;
    }

    ResampledSurface surface = resampledSurfaceAt(pixel, normalize(inRay), distanceToEye);
    RandomStream random = randomStream(pixel, CandidatesStream);
    Combining c = startCombining();

    // Step 1: the candidates from the alias table, each weighted by p̂ over its source probability.
    for (uint i = 0u; i < resampled.candidates; i++)
    {
        uint entry = min(uint(nextRandom(random) * float(sampled)), sampled - 1u);
        uvec4 alias = lightTable[2u + entry];
        uint light = nextRandom(random) < uintBitsToFloat(alias.z) ? alias.x : lightTable[2u + alias.y].x;
        float share = uintBitsToFloat(lightTable[2u + (light == alias.x ? entry : alias.y)].w);
        float target = targetOf(light, surface);
        addCandidate(c, light, target, target / share, 1.0, random);
    }

    // Step 2: the temporal reuse of the last frame's reservoir, its light remapped into this frame's list, with the biased
    // combine's 1 / M. The last reservoir lies on the same surface, which the surface test found, and the spatial reuse
    // left in it only a light that its surface sees. So in a still its domain is this pixel's after step 3, and 1 / M is
    // the unbiased 1 / Z. Only a light whose visibility changed since the last frame takes a bias, for at most the frames
    // that the cap of M keeps it.
    ivec2 texel = previousTexel(pixel);
    if (texel.x >= 0)
    {
        Reservoir last = unpackReservoir(texelFetch(readReservoirs, texel, 0).xy);
        uint light = holdsChoice(last) ? remappedLight(last.light) : NoLight;
        // A reservoir whose light is gone starts empty. One without a choice still stands for its M candidates, whose
        // light was 0: dropping them would keep only the lit choices, raise W and wash out the shadows.
        if (!holdsChoice(last) || light != NoLight)
        {
            float target = holdsChoice(last) ? targetOf(light, surface) : 0.0;
            float m = min(last.M, HistoryCandidates * float(resampled.candidates));
            addReservoir(c, last, light, target, m, random);
        }
    }

    // Step 3: the visibility reuse. A shadowed light does not spread to the neighbors.
    Reservoir r = finishCombining(c, c.r.M);
    if (holdsChoice(r) && visibilityOf(r.light, surface) == 0.0)
        r.W = 0.0;
    outReservoir = packReservoir(r);
}
