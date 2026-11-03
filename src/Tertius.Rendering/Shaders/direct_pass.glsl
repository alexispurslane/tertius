// The direct pass (docs/plans/temporal-renderer.md, section 7, "The two passes"): the direct signal's sample of each lit
// pixel in its two parts, the diffuse and the specular light of the main view's lights, each divided by its material
// factor and pre-exposed. No ambient light and no emission: the composite adds both. direct_every.frag takes every light
// with its shadows, as the loop of the forward passes does (EveryLight). direct_resampled.frag, which defines RESAMPLED,
// shades the light of the pixel's reservoir after the spatial reuse, plus every exact light, and writes R1.
#ifndef TERTIUS_DIRECT_PASS_GLSL
#define TERTIUS_DIRECT_PASS_GLSL

#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"
#ifdef RESAMPLED
#include "restir.glsl"
#include "reservoirs.glsl"
#endif

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outDiffuse;   // the direct signal's diffuse sample
layout(location = 1) out vec4 outSpecular;  // its specular sample
#ifdef RESAMPLED
layout(location = 2) out uvec2 outReservoir;  // R1

// The pass's constant in the random numbers.
const uint DirectStream = 0x2u;

// The radius of the disk of the spatial neighbors, in pixels at a render width of 1,280.
const float NeighborRadius = 16.0;

// DirectLightSetting.MaxSpatialNeighbors.
const uint MaxSpatialNeighbors = 8u;

// The surface of another pixel of the G-buffer, as the candidates pass saw it there.
ResampledSurface surfaceAtPixel(ivec2 pixel)
{
    vec3 direction = normalize(viewRayThrough(vec2(pixel) + 0.5));
    return resampledSurfaceAt(pixel, direction, texelFetch(gbufferDistance, pixel, 0).r);
}

// p̂ · V at the surface of a reused reservoir's choice, or 0 for a reservoir without a choice.
float reusedTarget(Reservoir r, ResampledSurface surface)
{
    return holdsChoice(r) ? visibleTarget(r.light, surface) : 0.0;
}

// Step 1, the spatial reuse: Bitterli's unbiased combine (Bitterli and others 2020, algorithm 6) of the pixel's reservoir
// of R0, r_0 at the pixel q_0, with the reservoirs r_i of R0 at the neighbors q_i that show the same surface. With p̂'_q
// the target times the visibility at q (visibleTarget), the combine keeps r_i's light y_i with the weight
// w_i = p̂'_q(y_i) · W_i · M_i, and
//
//     W = (Σ_i w_i) / (Z · p̂'_q(y)),  Z = Σ M_i over the i with p̂'_{q_i}(y) > 0,
//
// for the kept light y. Each r_i chose its light from the lights with p̂'_{q_i} > 0: its candidates had p̂_{q_i} > 0, and
// the visibility reuse emptied a shadowed choice. So Z counts the candidates whose source could give y, and the estimate
// f_q(y) · W is unbiased where each W_i is (ARCHITECTURE.md, "ReSTIR DI"). r_0 always counts, since y's weight at q is
// above 0. A neighbor's light that q does not see has a weight of 0, so it never takes the choice from a light that q
// sees, and the reservoir written to R1 holds a light that q sees.
Reservoir spatialReuse(ivec2 pixel, ResampledSurface surface, inout RandomStream random)
{
    Combining c = startCombining();
    Reservoir own = unpackReservoir(texelFetch(readReservoirs, pixel, 0).xy);
    addReservoir(c, own, own.light, reusedTarget(own, surface), own.M, random);

    // The neighbors that the combine takes, and the M of each, for Z.
    ivec2 taken[MaxSpatialNeighbors];
    float takenM[MaxSpatialNeighbors];
    uint count = 0u;
    SurfaceKey key = surfaceKeyAt(pixel);
    ivec2 last = textureSize(gbufferDistance, 0) - 1;
    float radius = NeighborRadius * frame.viewport.x / 1280.0;
    float turn = nextRandom(random);
    for (uint i = 0u; i < resampled.neighbors; i++)
    {
        float angle = 2.0 * PI * (turn + float(i) / float(resampled.neighbors));
        vec2 offset = radius * sqrt(nextRandom(random)) * vec2(cos(angle), sin(angle));
        ivec2 neighbor = clamp(pixel + ivec2(round(offset)), ivec2(0), last);
        if (neighbor == pixel || !sameSurface(key, surfaceKeyAt(neighbor)))
            continue;
        // A neighbor without a choice still stands for its M candidates.
        Reservoir other = unpackReservoir(texelFetch(readReservoirs, neighbor, 0).xy);
        addReservoir(c, other, other.light, reusedTarget(other, surface), other.M, random);
        taken[count] = neighbor;
        takenM[count] = other.M;
        count++;
    }

    // Z: one visibility test of the kept light at each neighbor's surface.
    float Z = own.M;
    if (c.chosenTarget > 0.0)
        for (uint i = 0u; i < count; i++)
            if (visibleTarget(c.r.light, surfaceAtPixel(taken[i])) > 0.0)
                Z += takenM[i];
    return finishCombining(c, Z);
}
#endif

void writeEmpty()
{
    outDiffuse = vec4(0.0);
    outSpecular = vec4(0.0);
#ifdef RESAMPLED
    outReservoir = packReservoir(emptyReservoir());
#endif
}

void main()
{
    // A helper invocation of a 2 × 2 quad can lie past the image's last column or row when the size is odd, and a fetch
    // there is undefined, so every read stays inside the image.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), textureSize(gbufferDistance, 0) - 1);
    vec3 direction = normalize(inRay);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    // Sky and impostors: no direct light. An impostor's light, ambient light included, is baked into its emission.
    if (distanceToEye <= 0.0 || texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
        writeEmpty();
        return;
    }

    // The position comes from the ray and the stored distance, so it does not depend on the projection of the
    // depth range that drew the pixel.
    vec3 position = direction * distanceToEye;
    vec3 V = -direction;
    ShadingInput s = materialAt(pixel);
    SurfaceLobes lobes = lobesOf(s, V);
#ifdef RESAMPLED
    ResampledSurface surface = resampledSurfaceAt(pixel, direction, distanceToEye);
    RandomStream random = randomStream(pixel, DirectStream);
    Reservoir r = spatialReuse(pixel, surface, random);
    // Step 2: the chosen light, its exact light times its visibility times W, and every exact light. The combine kept only
    // a light that the pixel sees, so R1 needs no visibility test of its own.
    DirectTerms direct;
    direct.diffuse = vec3(0.0);
    direct.specular = vec3(0.0);
    if (holdsChoice(r))
    {
        DirectTerms chosen = shadedLight(r.light, surface);
        direct.diffuse = chosen.diffuse * r.W;
        direct.specular = chosen.specular * r.W;
    }
    for (uint i = 0u; i < exactLights(); i++)
    {
        DirectTerms exact = shadedLight(exactLight(i), surface);
        direct.diffuse += exact.diffuse;
        direct.specular += exact.specular;
    }
    outReservoir = packReservoir(r);
#else
    DirectTerms direct = directLight(s, lobes, position, V, ShadowMapsMainView, MainView);
#endif
    MaterialFactors factors = materialFactors(s, lobes);
    float k = exposureScale();
    outDiffuse = vec4(demodulated(direct.diffuse, factors.diffuse, k), 1.0);
    outSpecular = vec4(demodulated(direct.specular, factors.specular, k), 1.0);
}

#endif
