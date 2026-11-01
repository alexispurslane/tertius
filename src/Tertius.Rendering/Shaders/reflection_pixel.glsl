// The pixel's side of the screen-space reflections (docs/plans/screen-space-reflections.md, "The method"): which pixels
// start a ray, the ray of a pixel, and the delta f · (W · C − a · k · T_s) that its ray's result adds to the lit image.
// reflection_trace.comp, reflection_half_trace.comp and reflection_upsample.comp share it.
#ifndef TERTIUS_REFLECTION_PIXEL_GLSL
#define TERTIUS_REFLECTION_PIXEL_GLSL

#include "frame.glsl"
#include "exposure.glsl"
// gbuffer_surface.glsl includes lighting.glsl after the G-buffer's bindings, which its shadows read.
#include "gbuffer_surface.glsl"
#include "reflection.glsl"

layout(push_constant, std430) uniform ReflectionPush
{
    float maxRoughness;
    uint maxSteps;
} reflection;

// A lit pixel that starts a ray, and what its ray and its delta need.
struct ReflectingPixel
{
    ShadingInput s;
    float alpha;            // the floored roughness squared
    vec3 V;                 // toward the eye
    vec3 toSurface;         // the unit view ray
    vec3 direction;         // the specular dominant direction d
    float distanceToEye;
    float viewDepth;
    float pixelFade;        // the roughness fade times the surface fade
};

// Whether the pixel starts a ray: not sky, not an impostor, whose light the atlas holds, and with a roughness fade and a
// surface fade above 0.
bool reflectingPixel(ivec2 pixel, out ReflectingPixel p)
{
    p.distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (p.distanceToEye <= 0.0 || texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
        return false;
    // surfaceAt gives the footprint f, which the cone reads too. The ambient pass took f where its light reads it, and
    // p elsewhere, which gives the same light (ambientSurfaceAt), so the delta's fallback is the term that it added.
    p.s = surfaceAt(pixel);
    float roughness = flooredRoughness(p.s.roughness);
    float roughnessFade = clamp(2.0 - 2.0 * roughness / reflection.maxRoughness, 0.0, 1.0);
    if (roughnessFade <= 0.0)
        return false;
    vec3 ray = viewRayThrough(vec2(pixel) + 0.5);
    p.toSurface = normalize(ray);
    p.V = -p.toSurface;
    p.alpha = roughness * roughness;
    p.direction = specularDominantDirection(p.s.normal, reflect(-p.V, p.s.normal), p.alpha);
    float surfaceCosine = dot(p.direction, p.s.geometricNormal);
    // A ray of any model, hair too, that points into its own geometric surface traces nothing, and one that grazes it
    // takes the surface fade's share (docs/plans/skin-and-hair.md, decision 35).
    if (surfaceCosine <= 0.0)
        return false;
    p.viewDepth = p.distanceToEye / length(ray);
    p.pixelFade = roughnessFade * min(surfaceCosine / SurfaceFadeCosine, 1.0);
    return true;
}

// The delta of the pixel for its ray's result: the pre-exposed radiance and the coverage of the ray's hit, both times the
// ray's own fade (the edge fade and 1 − q), or zeros for a miss.
vec3 reflectionDelta(ReflectingPixel p, vec4 rayResult)
{
    SurfaceLobes lobes = lobesOf(p.s, p.V);
    // The ambient specular term as the ambient pass wrote it, pre-exposed (the one place: ambientTerms).
    vec3 ambientSpecular = preExposed(ambientTerms(p.s, lobes, p.V).specular);
    return p.pixelFade * (specularWeight(lobes) * rayResult.rgb - rayResult.a * ambientSpecular);
}

#endif
