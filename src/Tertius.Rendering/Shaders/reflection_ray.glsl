// The ray of a reflecting pixel through the depth pyramid and its cone's read of the radiance pyramid
// (docs/plans/screen-space-reflections.md, "The method"), for reflection_trace.comp, reflection_half_trace.comp and the
// own rays of reflection_own_rays.comp.
#ifndef TERTIUS_REFLECTION_RAY_GLSL
#define TERTIUS_REFLECTION_RAY_GLSL

#include "reflection_pixel.glsl"

layout(set = 3, binding = 0) uniform texture2D depthPyramid;     // RG32F, mip m = level m + 1
layout(set = 3, binding = 1) uniform texture2D radiancePyramid;  // RGBA16F: premultiplied radiance, coverage
layout(set = 3, binding = 2) uniform sampler mipSampler;         // linear, linear between mips, clamped

// The result of the pixel's ray: the radiance pyramid's read at the hit and the cone level, seen through the haze from
// the eye, premultiplied radiance in RGB and coverage in A, both times the ray's fade, the edge fade and 1 − q, and the
// view depth of the hit in m. False for a ray that hits nothing. texelSide is the side of the trace's texel in pixels,
// the distance to the next ray: 1 at full resolution, 2 at half.
bool traceReflection(ReflectingPixel p, float texelSide, out vec4 rayResult, out float hitDepth)
{
    vec3 origin = p.toSurface * p.distanceToEye + p.s.geometricNormal * originOffset(p.viewDepth);
    vec2 hit;
    ivec2 hitPixel;
    float entryShare;
    ScreenRay screenRay = screenRayOf(origin, p.direction, p.viewDepth);
    if (!traceRay(depthPyramid, screenRay, reflection.maxSteps, hit, hitPixel, entryShare))
        return false;

    // The hit's surface point from its own distance, not from the ray's inverse depth, which is coarse far away.
    float hitDistance = texelFetch(gbufferDistance, hitPixel, 0).r;
    vec3 hitRay = viewRayThrough(hit);
    vec3 hitPoint = normalize(hitRay) * hitDistance;
    // The view ray's forward part is 1, so its length is at least 1.
    hitDepth = hitDistance / length(hitRay);
    float level = coneLevel(p.alpha, reflectionFootprintAt(p.pixel), texelSide, length(hitPoint - origin), hitDistance,
        float(textureQueryLevels(radiancePyramid) - 1));
    vec4 traced = textureLod(sampler2D(radiancePyramid, mipSampler), hit * frame.viewport.zw, level);
    // The pyramid holds the lit image from before the haze pass. The hit takes the haze between the eye and it, as the
    // environment faces give the map's reflection of it, and the haze pass then gives the mirror's own haze to both
    // (docs/plans/atmosphere.md, "The haze"). The hit's light is premultiplied, so its coverage is the cover. Both parts
    // are at most MaxRadiance, and so is their convex sum.
    vec3 hazed = throughAir(traced.rgb, vec3(traced.a), hitPoint, preExposed(frame.atmosphere.haze.rgb));
    rayResult = edgeFade(hit) * (1.0 - entryShare) * vec4(hazed, traced.a);
    return true;
}

// The delta that the pixel's own ray adds to its lit light, with a texel side of 1 pixel: the full-resolution trace's
// ray, which reflection_trace.comp traces at every pixel and reflection_own_rays.comp at each listed pixel. False where
// the pixel starts no ray or its ray hits nothing, which leaves the lit light as it is.
bool ownRayDelta(ivec2 pixel, out vec3 delta)
{
    ReflectingPixel p;
    vec4 rayResult;
    float hitDepth;
    if (!reflectingPixel(pixel, p) || !traceReflection(p, 1.0, rayResult, hitDepth))
        return false;
    delta = reflectionDelta(p, rayResult);
    return true;
}

#endif
