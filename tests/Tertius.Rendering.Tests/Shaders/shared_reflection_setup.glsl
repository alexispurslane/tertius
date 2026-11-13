// The setup of ResolveSharedReflectionTests: every pixel of a block takes one reflection ray along the mirror direction of
// the block's first pixel, with a light of 1 and a hit 1 m away. The includer defines OwnRays and RecordedDensityScale.
// With OwnRays, every pixel traces that ray as its own, recorded with the pixel's own density. Without it, the block holds
// the ray, recorded with its first pixel's density times RecordedDensityScale, and every pixel shares it.
#include "screen_trace.glsl"

layout(local_size_x = 8, local_size_y = 8) in;

RayRecord sharedReflection(vec3 direction, float density)
{
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    return recordOf(TracedRay(direction, density, vec3(1.0), 1.0, true, NoLightAge, false));
#else
    return recordOf(TracedRay(direction, density, vec3(1.0), 1.0, true));
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
#if OwnRays
    RayRecord record = sharedReflection(direction, reflectionDensity(tracingPixelAt(pixel), direction));
    imageStore(ownLight, ivec3(pixel, KindReflection), record.light);
    imageStore(ownRay, ivec3(pixel, KindReflection), record.ray);
    imageStore(rayPlan, pixel, uvec4(PlanOwnReflection));
#else
    if (all(equal(pixel, first)))
    {
        RayRecord record = sharedReflection(direction, reflectionDensity(c, direction) * RecordedDensityScale);
        imageStore(blockLight, ivec3(pixel / 2, KindReflection), record.light);
        imageStore(blockRay, ivec3(pixel / 2, KindReflection), record.ray);
    }
    imageStore(rayPlan, pixel, uvec4(0u));
#endif
}
