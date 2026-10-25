// The hemisphere ambient light's up, shared by the G-buffer pass, which stores both sky shares, and by the passes that
// shade a surface themselves.
#ifndef TERTIUS_HEMISPHERE_GLSL
#define TERTIUS_HEMISPHERE_GLSL

#include "brdf.glsl"

// The unit up of the hemisphere ambient light at `position`. `hemisphere` is the instance's (frame.glsl): up along xyz
// for w 1, away from the point xyz for w 2. Both sky shares read it, so the two cannot disagree about it.
vec3 hemisphereUp(vec4 hemisphere, vec3 position)
{
    vec3 away = position - hemisphere.xyz;
    return hemisphere.w < 1.5 ? hemisphere.xyz : away * inversesqrt(max(dot(away, away), 1e-20));
}

// How much of the hemisphere ambient light a surface at `position` with `normal` gets from the sky: 1 facing straight
// up, 0 straight down, 0.5 sideways. A hemisphere with w 0 has no up and gives 0.5 everywhere.
float skyFraction(vec4 hemisphere, vec3 position, vec3 normal)
{
    if (hemisphere.w < 0.5)
        return 0.5;
    return clamp(dot(normal, hemisphereUp(hemisphere, position)) * 0.5 + 0.5, 0.0, 1.0);
}

// How much of the light that the specular lobe gathers comes from the sky: the lobe-sky table at the tilt of the lobe's
// dominant direction (docs/plans/pbr-brdf.md, "The ambient light's specular part"). V points from the surface to the
// eye. A hemisphere with w 0 gives 0.5 everywhere, as skyFraction does.
float lobeSkyFraction(vec4 hemisphere, vec3 position, vec3 normal, vec3 V, float roughness)
{
    if (hemisphere.w < 0.5)
        return 0.5;
    float floored = flooredRoughness(roughness);
    vec3 dominant = specularDominantDirection(normal, reflect(-V, normal), floored * floored);
    return lobeSkyAt(dot(dominant, hemisphereUp(hemisphere, position)), floored);
}

#endif
