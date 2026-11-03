// ReSTIR DI (docs/plans/temporal-renderer.md, section 7): the light table, the reservoirs, the random numbers and the
// target function that the two passes share, light_candidates.frag and the direct pass under ResampledLights
// (direct_pass.glsl). A reservoir stands for M candidates and holds one chosen light with its weight W.
#ifndef TERTIUS_RESTIR_GLSL
#define TERTIUS_RESTIR_GLSL

#include "temporal.glsl"
#include "meter.glsl"

// ReSTIR's light table (LightTable.cs, FrameResources.WriteLightTable): texel 0 holds x the sampled lights n, y the exact
// lights, z the length of the remap and w 0; texel 1 the exact lights' indices in the light list; texels 2 to n + 1 the
// alias table's entries, x the light's index, y the entry of its alias, z the bits of its threshold and w the bits of its
// share; then the remap, four indices per texel.
layout(set = 0, binding = 19, std430) readonly buffer LightTableBlock { uvec4 lightTable[]; };

// LightTable.NoLight: the index of no light, in a reservoir and in the remap.
const uint NoLight = 0xFFFFu;

// ResampledPushConstants.
layout(push_constant, std430) uniform ResampledPush
{
    uint drawIndex;   // FrameHistory's draw index, which turns the random numbers in each draw
    uint candidates;  // ResampledLights.Candidates
    uint neighbors;   // ResampledLights.SpatialNeighbors
    uint unused;
} resampled;

uint sampledLights()
{
    return lightTable[0].x;
}

uint exactLights()
{
    return lightTable[0].y;
}

uint exactLight(uint i)
{
    return lightTable[1][i];
}

// The index in this frame's list of the light that held `last` in the last drawn frame's list, or NoLight.
uint remappedLight(uint last)
{
    if (last >= lightTable[0].z)
        return NoLight;
    return lightTable[2u + sampledLights() + last / 4u][last % 4u];
}

// pcg3d (Jarzynski and Olano 2020): three random words from three words.
uvec3 pcg3d(uvec3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

// A stream of random numbers of one pixel in one draw of one pass: pcg3d of the pixel and the draw index, with a pass's
// constant and a counter in the third word. So each pixel, draw and pass takes its own numbers, the same on every run.
struct RandomStream
{
    uvec3 seed;
    uint counter;
};

RandomStream randomStream(ivec2 pixel, uint pass)
{
    RandomStream r;
    r.seed = uvec3(uvec2(pixel), resampled.drawIndex * 0x9E3779B9u ^ pass);
    r.counter = 0u;
    return r;
}

// The next random number of the stream, from 0 to 1 and below 1: the top 24 bits of a word.
float nextRandom(inout RandomStream r)
{
    uvec3 v = pcg3d(r.seed + uvec3(0u, 0u, r.counter++ * 0x632BE5ABu));
    return float(v.x >> 8) * (1.0 / 16777216.0);
}

// A reservoir unpacked from its RG32UI texel: x the light in the low 16 bits and M in the high 16, y the bits of W. It holds
// a choice exactly where W > 0 (holdsChoice), and its light means something only there. A reservoir without a choice, whose
// candidates all had a target of 0 or whose choice a visibility test emptied, still stands for its M candidates.
struct Reservoir
{
    uint light;
    float M;
    float W;
};

Reservoir emptyReservoir()
{
    Reservoir r;
    r.light = NoLight;
    r.M = 0.0;
    r.W = 0.0;
    return r;
}

// Whether the reservoir holds a choice: the one test of it, also false for a W that is NaN.
bool holdsChoice(Reservoir r)
{
    return r.W > 0.0;
}

Reservoir unpackReservoir(uvec2 texel)
{
    Reservoir r;
    r.light = texel.x & 0xFFFFu;
    r.M = float(texel.x >> 16);
    r.W = uintBitsToFloat(texel.y);
    return r;
}

uvec2 packReservoir(Reservoir r)
{
    return uvec2(r.light | (uint(min(r.M, 65535.0)) << 16), floatBitsToUint(r.W));
}

// A reservoir in the making: the sum of its candidates' weights, the chosen light and its target.
struct Combining
{
    Reservoir r;
    float weightSum;
    float chosenTarget;
};

Combining startCombining()
{
    Combining c;
    c.r = emptyReservoir();
    c.weightSum = 0.0;
    c.chosenTarget = 0.0;
    return c;
}

// Weighted reservoir sampling: adds a candidate of weight w that stands for m candidates, and keeps it with the probability
// w over the new sum.
void addCandidate(inout Combining c, uint light, float target, float w, float m, inout RandomStream random)
{
    c.weightSum += w;
    c.r.M += m;
    if (w > 0.0 && nextRandom(random) * c.weightSum < w)
    {
        c.r.light = light;
        c.chosenTarget = target;
    }
}

// The reservoir that the candidates make: W is the sum of the weights over M, divided by the chosen light's target
// (Bitterli's biased combine, with weights 1 / M). A W that is not finite, from a target near the least float, makes no
// choice, so that no reuse carries it on.
Reservoir finishCombining(Combining c)
{
    Reservoir r = c.r;
    r.W = c.chosenTarget > 0.0 && r.M > 0.0 ? c.weightSum / (r.M * c.chosenTarget) : 0.0;
    if (isinf(r.W) || isnan(r.W))
        r.W = 0.0;
    return r;
}

// The surface of one pixel as both passes shade it: its shading input, its lobes, its point relative to the eye, V, and
// the model's choices of respond and lightIrradiance.
struct ResampledSurface
{
    ShadingInput s;
    SurfaceLobes lobes;
    vec3 position;
    vec3 V;
    bool fiber;
    bool tinted;
};

ResampledSurface resampledSurfaceAt(ivec2 pixel, vec3 direction, float distanceToEye)
{
    ResampledSurface surface;
    surface.s = materialAt(pixel);
    surface.V = -direction;
    surface.position = direction * distanceToEye;
    surface.lobes = lobesOf(surface.s, surface.V);
    surface.fiber = !dropsFibers && surface.s.model == ShadingHair;
    surface.tinted = frame.counts.y != NoAirSun;
    return surface;
}

// p̂ of a light at the surface: the luminance of its unshadowed light, its two parts with their factors in them, or 0 where
// the surface takes none of it or where the bounds of its shadow tile show it fully shadowed.
float targetOf(uint light, ResampledSurface surface)
{
    LightPath p;
    if (!lightPath(light, surface.fiber, surface.tinted, surface.s, surface.position, p))
        return 0.0;
    if (lights[light].shadow.x == AtlasTilesSlot
        && tileFullyShadowed(lights[light], surface.position, p.L, p.d, p.shadowNormal, p.sinTheta))
        return 0.0;
    DirectTerms terms = pathLight(light, surface.fiber, surface.s, surface.lobes, surface.V, p);
    return luminance(terms.diffuse + terms.specular);
}

// Combines a reused reservoir `other`, whose choice is `light` in this frame's list, that stands for `m` candidates: its
// choice with p̂ at the surface and the weight p̂ · W · m. One without a choice adds only its m candidates, whose light was 0.
void addReservoir(
    inout Combining c, Reservoir other, uint light, float m, ResampledSurface surface, inout RandomStream random)
{
    float target = holdsChoice(other) ? targetOf(light, surface) : 0.0;
    addCandidate(c, light, target, target * other.W * m, m, random);
}

// The share of a light that its shadows let through at the surface, from the frame's taps, or 0 where the surface takes
// none of it.
float visibilityOf(uint light, ResampledSurface surface)
{
    LightPath p;
    if (!lightPath(light, surface.fiber, surface.tinted, surface.s, surface.position, p))
        return 0.0;
    return pathVisibility(light, p, surface.position, ShadowMapsMainView);
}

// The light of a light at the surface with its shadows, from the frame's taps.
DirectTerms shadedLight(uint light, ResampledSurface surface)
{
    DirectTerms terms;
    terms.diffuse = vec3(0.0);
    terms.specular = vec3(0.0);
    LightPath p;
    if (!lightPath(light, surface.fiber, surface.tinted, surface.s, surface.position, p))
        return terms;
    float visibility = pathVisibility(light, p, surface.position, ShadowMapsMainView);
    if (visibility > 0.0)
    {
        terms = pathLight(light, surface.fiber, surface.s, surface.lobes, surface.V, p);
        terms.diffuse *= visibility;
        terms.specular *= visibility;
    }
    return terms;
}

#endif
