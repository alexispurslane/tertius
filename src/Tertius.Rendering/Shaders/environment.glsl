// The scene environment map's light, for the ambient source of lighting.glsl and the lit puffs
// (docs/plans/environment-map.md, "Reading the map"). The map holds plain radiance in camera-relative world axes.
#ifndef TERTIUS_ENVIRONMENT_GLSL
#define TERTIUS_ENVIRONMENT_GLSL

#include "frame.glsl"
#include "sky.glsl"
#include "spherical_harmonics.glsl"

// ShaderConstants.EnvironmentMap: true while the renderer's setting is SceneEnvironmentMap, fixed for its life.
layout(constant_id = 0) const bool environmentMapOn = false;

// RGBA16F: level k holds roughness k / (L − 1), alpha the open-sky share. Partially bound: empty with the map off.
layout(set = 0, binding = 12) uniform textureCube environmentSpecularMap;
// Trilinear, clamped, every level.
layout(set = 0, binding = 13) uniform sampler environmentSampler;
// E_lm in xyz, in the order of spherical_harmonics.glsl. Partially bound: empty with the map off.
layout(set = 0, binding = 14, std430) readonly buffer EnvironmentIrradianceBlock { vec4 environmentIrradiance[9]; };

// D(n): the ambient diffuse radiance on a surface with the unit normal n, max(Σ E_lm Y_lm(n), 0) / π.
vec3 environmentDiffuse(vec3 normal)
{
    float basis[9];
    shBasis(normal, basis);
    vec3 irradiance = vec3(0.0);
    for (int i = 0; i < 9; i++)
        irradiance += environmentIrradiance[i].xyz * basis[i];
    return max(irradiance, vec3(0.0)) / PI;
}

// S(d, r): the specular map's light along the lobe of the floored roughness r at the unit direction d, plus the reflected
// stars for the footprint f, times the open-sky share there. L comes from the image, so no other store holds it.
vec3 environmentSpecular(vec3 d, float roughness, float footprint)
{
    float levels = float(textureQueryLevels(samplerCube(environmentSpecularMap, environmentSampler)));
    vec4 lobe = textureLod(samplerCube(environmentSpecularMap, environmentSampler), d, roughness * (levels - 1.0));
    return lobe.rgb + lobe.a * reflectedStars(d, starWidth(footprint, roughness * roughness));
}

// The mean of the map's radiance over every direction, E_00 Y_00 / π, without the stars: the lit puffs' ambient light.
vec3 environmentMeanRadiance()
{
    const float y00 = 0.28209479177387814;   // 1 / (2 √π)
    return environmentIrradiance[0].xyz * (y00 / PI);
}

#endif
