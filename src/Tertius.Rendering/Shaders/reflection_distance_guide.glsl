#ifndef TERTIUS_REFLECTION_DISTANCE_GUIDE_GLSL
#define TERTIUS_REFLECTION_DISTANCE_GUIDE_GLSL

layout(constant_id = 10) const bool reflectionSharingPrototype = false;

vec2 reflectionDistanceSample(float hitDistance)
{
    if (hitDistance <= 0.0 || hitDistance >= 65504.0 || isnan(hitDistance) || isinf(hitDistance))
        return vec2(0.0);
    return vec2(hitDistance, 1.0);
}

#endif
