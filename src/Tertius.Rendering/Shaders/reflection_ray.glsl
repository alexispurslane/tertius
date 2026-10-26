// The ray of a reflecting pixel through the depth pyramid and its cone's read of the radiance pyramid
// (docs/plans/screen-space-reflections.md, "The method"), for reflection_trace.comp and reflection_half_trace.comp.
#ifndef TERTIUS_REFLECTION_RAY_GLSL
#define TERTIUS_REFLECTION_RAY_GLSL

#include "reflection_pixel.glsl"

layout(set = 3, binding = 0) uniform texture2D depthPyramid;     // RG32F, mip m = level m + 1
layout(set = 3, binding = 1) uniform texture2D radiancePyramid;  // RGBA16F: premultiplied radiance, coverage
layout(set = 3, binding = 2) uniform sampler mipSampler;         // linear, linear between mips, clamped

// The result of the pixel's ray: the radiance pyramid's read at the hit and the cone level, premultiplied radiance in
// RGB and coverage in A, both times the ray's fade, the edge fade and 1 − q. False for a ray that hits nothing.
// texelSide is the side of the trace's texel in pixels, the distance to the next ray: 1 at full resolution, 2 at half.
bool traceReflection(ReflectingPixel p, float texelSide, out vec4 rayResult)
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
    vec3 hitPoint = normalize(viewRayThrough(hit)) * hitDistance;
    float level = coneLevel(p.alpha, p.s.reflectionFootprint, texelSide, length(hitPoint - origin), hitDistance,
        float(textureQueryLevels(radiancePyramid) - 1));
    vec4 traced = textureLod(sampler2D(radiancePyramid, mipSampler), hit * frame.viewport.zw, level);
    rayResult = edgeFade(hit) * (1.0 - entryShare) * traced;
    return true;
}

#endif
