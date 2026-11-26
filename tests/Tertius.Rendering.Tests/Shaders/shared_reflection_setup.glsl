// The setup of ResolveSharedReflectionTests: every pixel of a block takes one reflection ray along the mirror direction of
// the block's first pixel, with a light of 1 and a hit 1 m away. The includer defines OwnRays and RecordedDensityScale, and
// may define GuideShare, the guided share at which the block's ray was drawn, 0 by default. The ray's guided density is
// GuidedDensityScale times its first pixel's lobe density. With OwnRays, every pixel traces that ray as its own, recorded
// with the pixel's own mixture density at GuideShare. Without it, the block holds the ray, recorded with its first
// pixel's density times RecordedDensityScale, and its guide holds the guided density and GuideShare, and every pixel
// shares it.
#include "screen_trace.glsl"

#ifndef GuideShare
#define GuideShare 0.0
#endif
const float GuidedDensityScale = 3.0;

layout(local_size_x = 8, local_size_y = 8) in;

RayRecord sharedReflection(vec3 direction, float density)
{
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    return recordOf(TracedRay(direction, density, density, 0.0, 0.0, vec3(1.0), 1.0, true, NoLightAge, false));
#else
    return recordOf(TracedRay(direction, density, density, 0.0, 0.0, vec3(1.0), 1.0, true));
#endif
}

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(vec2(pixel), frame.viewport.xy)))
        return;
    ivec2 first = 2 * (pixel / 2);
    if (!tracedSurface(pixel) || !tracedSurface(first))
    {
        imageStore(rayPlan, pixel, uvec4(0u));
        return;
    }
    TracingPixel c = tracingPixelAt(first);
    vec3 direction = directionOfCode(directionCodeOf(reflect(-c.V, c.normal)));
    float guided = GuidedDensityScale * reflectionDensity(c, direction);
#if OwnRays
    float own = reflectionDensity(tracingPixelAt(pixel), direction);
    RayRecord record = sharedReflection(direction, mixtureDensity(GuideShare, own, guided));
    imageStore(ownLight, pixel, record.light);
    imageStore(ownRay, pixel, record.ray);
    imageStore(rayPlan, pixel, uvec4(PlanOwnReflection));
#else
    if (all(equal(pixel, first)))
    {
        RayRecord record = sharedReflection(direction, reflectionDensity(c, direction) * RecordedDensityScale);
        imageStore(blockLight, pixel / 2, record.light);
        imageStore(blockRay, pixel / 2, record.ray);
        imageStore(blockGuide, pixel / 2, vec4(guided, 0.0, 0.0, GuideShare));
    }
    imageStore(rayPlan, pixel, uvec4(0u));
#endif
}
