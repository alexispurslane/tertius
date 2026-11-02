// The scene environment maps' light, for the ambient source of lighting.glsl and the lit puffs
// (docs/plans/environment-map.md, "Reading the map", and docs/plans/environment-maps-per-frame.md, "The readers"). Each
// chain frame's map holds plain radiance in the axes of its frame, so every reader names its map and turns its direction
// into the map's axes first.
#ifndef TERTIUS_ENVIRONMENT_GLSL
#define TERTIUS_ENVIRONMENT_GLSL

#include "cube_faces.glsl"
#include "frame.glsl"
#include "sky.glsl"

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
// RGBA16F, a cube array whose cube i is map i's irradiance cube: level 0 holds D at each texel's direction, 16 texels on a
// side, alpha the open-sky share that a Lambert surface sees, and each later level the mean of its children over their
// solid angles, down to 2 texels on a side. Sampled with environmentSampler. Partially bound: empty with the map off.
layout(set = 0, binding = 14) uniform textureCubeArray environmentIrradianceMap;

// A world direction in the axes of map `map`: the inverse of R_i, frame.mapAxes[map], by the quaternion sandwich with the
// conjugate. The batcher keeps R_i a unit quaternion, so the turn keeps a unit vector unit, and the identity turns v to
// itself exactly, because its vector part is 0.
vec3 inMapAxes(uint map, vec3 v)
{
    vec4 axes = frame.mapAxes[map];
    vec3 u = -axes.xyz;
    return v + 2.0 * cross(u, cross(u, v) + axes.w * v);
}

// D(n): the ambient diffuse radiance of map `map` on a surface with the unit normal n, from level 0 of its irradiance cube
// at n, filtered across the cube's texels and faces.
vec3 environmentDiffuse(uint map, vec3 normal)
{
    return textureLod(
        samplerCubeArray(environmentIrradianceMap, environmentSampler),
        vec4(inMapAxes(map, normal), float(map)),
        0.0).rgb;
}

// Map `map`'s specular map along the lobe of roughness r, from 0 to 1, at the unit world direction d: the light in rgb
// and the open-sky share in a. The layer coordinate float(map) is an integer, which Vulkan rounds to itself.
vec4 environmentLobe(uint map, vec3 d, float roughness)
{
    float levels = float(textureQueryLevels(samplerCubeArray(environmentSpecularMap, environmentSampler)));
    return textureLod(
        samplerCubeArray(environmentSpecularMap, environmentSampler),
        vec4(inMapAxes(map, d), float(map)),
        roughness * (levels - 1.0));
}

// S(d, r): map `map`'s specular light along the lobe of the floored roughness r at the unit direction d, plus the
// reflected stars for the footprint f, times the open-sky share there. The stars stay in world axes at d. L comes from the
// image, so no other store holds it.
vec3 environmentSpecular(uint map, vec3 d, float roughness, float footprint)
{
    vec4 lobe = environmentLobe(map, d, roughness);
    // Where the lobe sees no open sky, its share is 0, and 0 times the reflected stars, which are finite, is a zero,
    // whose sum with lobe.rgb equals lobe.rgb, so the stars' cells are not hashed.
    if (skipsTermsThatAddNothing && lobe.a == 0.0)
        return lobe.rgb;
    return lobe.rgb + lobe.a * reflectedStars(d, starWidth(footprint, roughness * roughness));
}

// α of the specular map's lobe per rad of the width of the part of the sky that a read stands for: GgxLobe.AlphaPerWidth.
const float LobeAlphaPerWidth = 0.12108;

// The lower bound on the roughness of a read for a part, GgxLobe.MinPieceRoughness: a sharper read lets a small bright
// part of the map show in some texels and not in their neighbors (docs/plans/screen-space-illumination.md, decision 42).
const float MinPieceRoughness = 0.5;

// The roughness of the specular map's lobe that blurs a read over a part of the sky `width` rad wide:
// max(sqrt(LobeAlphaPerWidth · width), MinPieceRoughness) (docs/plans/screen-space-illumination.md, decisions 41 and
// 42). Only a part wider than 2.07 rad keeps 95 % of the lobe's weight on itself. A narrower part takes the bound's
// lobe, which puts up to 11 % of its weight beyond either end of a part 0.95 rad wide and 24 % beyond a part 0.5 rad
// wide. GgxLobe.RoughnessForWidth is the C# copy.
float roughnessForWidth(float width)
{
    return max(sqrt(LobeAlphaPerWidth * width), MinPieceRoughness);
}

// The mean of map `map`'s radiance over every direction, without the stars: the lit puffs' ambient light. It is the mean
// of D over every direction, which the six faces of the irradiance cube's last level hold, a face's mean at its center:
// there the four texels of a face of 2 texels meet, so the filter weighs each by a quarter, and each spans a quarter of
// the face. The mean over the sphere does not depend on the map's axes, so the reads stay in them.
vec3 environmentMeanRadiance(uint map)
{
    float last = float(textureQueryLevels(samplerCubeArray(environmentIrradianceMap, environmentSampler)) - 1);
    vec3 sum = vec3(0.0);
    for (uint face = 0u; face < 6u; face++)
        sum += textureLod(
            samplerCubeArray(environmentIrradianceMap, environmentSampler),
            vec4(faceBasis(face).forward, float(map)),
            last).rgb;
    return sum / 6.0;
}

#endif
