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

// Step 1, the spatial reuse: the pixel's reservoir of R0 combined with its neighbors' that show the same surface.
Reservoir spatialReuse(ivec2 pixel, ResampledSurface surface, inout RandomStream random)
{
    Combining c = startCombining();
    Reservoir own = unpackReservoir(texelFetch(readReservoirs, pixel, 0).xy);
    addReservoir(c, own, own.light, own.M, surface, random);

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
        // A neighbor without a choice still stands for its M candidates (Bitterli's biased combine).
        Reservoir other = unpackReservoir(texelFetch(readReservoirs, neighbor, 0).xy);
        addReservoir(c, other, other.light, other.M, surface, random);
    }
    return finishCombining(c);
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
    // Step 2: the chosen light, its exact light times its visibility times W, and every exact light.
    DirectTerms direct;
    direct.diffuse = vec3(0.0);
    direct.specular = vec3(0.0);
    if (holdsChoice(r))
    {
        DirectTerms chosen = shadedLight(r.light, surface);
        direct.diffuse = chosen.diffuse * r.W;
        direct.specular = chosen.specular * r.W;
        // Step 3: a shadowed choice keeps its M and loses its weight.
        if (chosen.diffuse + chosen.specular == vec3(0.0))
            r.W = 0.0;
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
