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

// The constant of the neighbors' places in the random numbers.
const uint NeighborStream = 0x3u;

// The place of neighbor i of `neighbors` on the disk around the pixel, turned by `turn`, from the next number of `places`.
ivec2 neighborPlace(ivec2 pixel, uint i, float turn, inout RandomStream places)
{
    float radius = NeighborRadius * frame.viewport.x / 1280.0;
    float angle = 2.0 * PI * (turn + float(i) / float(resampled.neighbors));
    vec2 offset = radius * sqrt(nextRandom(places)) * vec2(cos(angle), sin(angle));
    return clamp(pixel + ivec2(round(offset)), ivec2(0), textureSize(gbufferDistance, 0) - 1);
}

// The surface of another pixel of the G-buffer, as the candidates pass saw it there.
ResampledSurface surfaceAtPixel(ivec2 pixel)
{
    vec3 direction = normalize(viewRayThrough(vec2(pixel) + 0.5));
    return resampledSurfaceAt(pixel, direction, texelFetch(gbufferDistance, pixel, 0).r);
}

// The light of a reused reservoir's choice at the surface with its shadows, or none for a reservoir without a choice.
DirectTerms reusedLight(Reservoir r, ResampledSurface surface)
{
    if (holdsChoice(r))
        return shadedLight(r.light, surface);
    DirectTerms none;
    none.diffuse = vec3(0.0);
    none.specular = vec3(0.0);
    return none;
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
// above 0, and so does a neighbor whose own choice is y, since its pass found y's p̂' above 0. A neighbor's light that q
// does not see has a weight of 0, so it never takes the choice from a light that q sees, and the reservoir written to R1
// holds a light that q sees. `chosen` is the kept light's light at q with its shadows, f_q(y), which p̂'_q(y) measures.
Reservoir spatialReuse(ivec2 pixel, ResampledSurface surface, inout RandomStream random, out DirectTerms chosen)
{
    Combining c = startCombining();
    Reservoir own = unpackReservoir(texelFetch(readReservoirs, pixel, 0).xy);
    DirectTerms ownLight = reusedLight(own, surface);
    chosen = ownLight;
    addReservoir(c, own, own.light, visibleTarget(ownLight), own.M, random);

    // The neighbors' places take their own stream, so that the test of Z walks them again: bit i of `taken` marks a
    // neighbor that the combine took.
    RandomStream places = randomStream(pixel, NeighborStream);
    float turn = nextRandom(places);
    RandomStream placesAgain = places;
    uint taken = 0u;
    SurfaceKey key = surfaceKeyAt(pixel);
    for (uint i = 0u; i < resampled.neighbors; i++)
    {
        ivec2 neighbor = neighborPlace(pixel, i, turn, places);
        if (neighbor == pixel || !sameSurface(key, surfaceKeyAt(neighbor)))
            continue;
        // A neighbor without a choice still stands for its M candidates. One with the pixel's own choice takes its light.
        Reservoir other = unpackReservoir(texelFetch(readReservoirs, neighbor, 0).xy);
        bool ownChoice = holdsChoice(other) && holdsChoice(own) && other.light == own.light;
        DirectTerms otherLight = ownChoice ? ownLight : reusedLight(other, surface);
        if (addReservoir(c, other, other.light, visibleTarget(otherLight), other.M, random))
            chosen = otherLight;
        taken |= 1u << i;
    }

    // Z: a visibility test of the kept light at the surface of each neighbor whose own choice is another light.
    float Z = own.M;
    for (uint i = 0u; i < resampled.neighbors; i++)
    {
        ivec2 neighbor = neighborPlace(pixel, i, turn, placesAgain);
        if ((taken & (1u << i)) == 0u || c.chosenTarget <= 0.0)
            continue;
        Reservoir other = unpackReservoir(texelFetch(readReservoirs, neighbor, 0).xy);
        if ((holdsChoice(other) && other.light == c.r.light)
            || visibleTarget(shadedLight(c.r.light, surfaceAtPixel(neighbor))) > 0.0)
            Z += other.M;
    }
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
    DirectTerms chosen;
    Reservoir r = spatialReuse(pixel, surface, random, chosen);
    // Step 2: the chosen light, its exact light times its visibility times W, and every exact light. The combine kept only
    // a light that the pixel sees, so R1 needs no visibility test of its own.
    DirectTerms direct;
    direct.diffuse = vec3(0.0);
    direct.specular = vec3(0.0);
    if (holdsChoice(r))
    {
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
