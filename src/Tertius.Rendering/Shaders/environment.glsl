// The scene environment maps' light, for the ambient source of lighting.glsl and the lit puffs
// (docs/plans/environment-map.md, "Reading the map", and docs/plans/environment-maps-per-frame.md, "The readers"). Each
// chain frame's map holds plain radiance in the axes of its frame, so every reader names its map and turns its direction
// into the map's axes first.
#ifndef TERTIUS_ENVIRONMENT_GLSL
#define TERTIUS_ENVIRONMENT_GLSL

#include "frame.glsl"
#include "sky.glsl"
#include "spherical_harmonics.glsl"

// subgroupBroadcastFirst: a device without it in fragment and compute shaders is refused (VulkanDevice).
#extension GL_KHR_shader_subgroup_ballot : require

// ShaderConstants.EnvironmentMap: true while the renderer's setting is SceneEnvironmentMap, fixed for its life.
layout(constant_id = 0) const bool environmentMapOn = false;

// Whether the ambient light skips the work of its terms that add nothing to the image (docs/plans/environment-map.md,
// "Cost"). A test build defines TERTIUS_EVERY_AMBIENT_TERM for its reference image, which computes every term
// (AmbientSkipsTests); the engine's build never does.
#ifdef TERTIUS_EVERY_AMBIENT_TERM
const bool skipsTermsThatAddNothing = false;
#else
const bool skipsTermsThatAddNothing = true;
#endif

// RGBA16F, a cube array whose cube i is map i: level k holds roughness k / (L − 1), alpha the open-sky share. Partially
// bound: empty with the map off.
layout(set = 0, binding = 12) uniform textureCubeArray environmentSpecularMap;
// Trilinear, clamped, every level.
layout(set = 0, binding = 13) uniform sampler environmentSampler;
// E_lm in xyz, in the order of spherical_harmonics.glsl: map i's nine coefficients from 9i on. Partially bound: empty
// with the map off.
layout(set = 0, binding = 14, std430) readonly buffer EnvironmentIrradianceBlock { vec4 environmentIrradiance[]; };

// A world direction in the axes of map `map`: the inverse of R_i, frame.mapAxes[map], by the quaternion sandwich with the
// conjugate. The batcher keeps R_i a unit quaternion, so the turn keeps a unit vector unit, and the identity turns v to
// itself exactly, because its vector part is 0.
vec3 inMapAxes(uint map, vec3 v)
{
    vec4 axes = frame.mapAxes[map];
    vec3 u = -axes.xyz;
    return v + 2.0 * cross(u, cross(u, v) + axes.w * v);
}

// D(n): the ambient diffuse radiance of map `map` on a surface with the unit normal n, max(Σ E_lm Y_lm(n), 0) / π.
// The coefficients are read at a map that is uniform over the subgroup (ARCHITECTURE.md, "Reading the map"): each turn of
// the loop serves the invocations whose map is the first active invocation's, so a subgroup whose pixels share one map
// takes one turn. Read at each invocation's own map, the nine coefficients took registers per invocation, and the
// lighting pass, now the ambient pass, ran at a lower occupancy.
vec3 environmentDiffuse(uint map, vec3 normal)
{
    float basis[9];
    shBasis(inMapAxes(map, normal), basis);
    vec3 irradiance = vec3(0.0);
    for (;;)
    {
        uint subgroupMap = subgroupBroadcastFirst(map);
        if (subgroupMap == map)
        {
            uint first = 9u * subgroupMap;
            for (uint i = 0u; i < 9u; i++)
                irradiance += environmentIrradiance[first + i].xyz * basis[i];
            break;
        }
    }
    return max(irradiance, vec3(0.0)) / PI;
}

// S(d, r): map `map`'s specular light along the lobe of the floored roughness r at the unit direction d, plus the
// reflected stars for the footprint f, times the open-sky share there. The stars stay in world axes at d. L comes from the
// image, so no other store holds it. The layer coordinate float(map) is an integer, which Vulkan rounds to itself.
vec3 environmentSpecular(uint map, vec3 d, float roughness, float footprint)
{
    float levels = float(textureQueryLevels(samplerCubeArray(environmentSpecularMap, environmentSampler)));
    vec4 lobe = textureLod(
        samplerCubeArray(environmentSpecularMap, environmentSampler),
        vec4(inMapAxes(map, d), float(map)),
        roughness * (levels - 1.0));
    // Where the lobe sees no open sky, its share is 0, and 0 times the reflected stars, which are finite, is a zero,
    // whose sum with lobe.rgb equals lobe.rgb, so the stars' cells are not hashed.
    if (skipsTermsThatAddNothing && lobe.a == 0.0)
        return lobe.rgb;
    return lobe.rgb + lobe.a * reflectedStars(d, starWidth(footprint, roughness * roughness));
}

// The mean of map `map`'s radiance over every direction, E_00 Y_00 / π, without the stars: the lit puffs' ambient light.
// E_00 does not depend on the map's axes.
vec3 environmentMeanRadiance(uint map)
{
    const float y00 = 0.28209479177387814;   // 1 / (2 √π)
    return environmentIrradiance[9u * map].xyz * (y00 / PI);
}

#endif
