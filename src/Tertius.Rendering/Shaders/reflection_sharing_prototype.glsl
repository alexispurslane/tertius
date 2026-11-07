#ifndef TERTIUS_REFLECTION_SHARING_PROTOTYPE_GLSL
#define TERTIUS_REFLECTION_SHARING_PROTOTYPE_GLSL

#include "reflection_distance_guide.glsl"
layout(set = 3, binding = 4, rgba16f) uniform readonly image2D reflectionHitSample;
layout(set = 3, binding = 5, rgba16f) uniform readonly image2D reflectionDistanceHistory;

struct ReflectionSharingReceiver
{
    ShadingInput surface;
    vec3 point;
    float distance;
    float radius;
    float support;
    ivec2 pixel;
};

float reflectionSharingRadiusAt(ivec2 pixel, ShadingInput surface, vec3 point)
{
    vec2 guide = imageLoad(reflectionDistanceHistory, pixel).rg;
    float hitDistance = guide.g > 0.0 ? guide.r / guide.g : 0.0;
    if (hitDistance <= 0.0 || hitDistance >= 65504.0 || isnan(hitDistance) || isinf(hitDistance))
        return 0.0;
    float viewDepth = dot(point, frame.cameraForward.xyz);
    if (viewDepth <= 0.0 || isnan(viewDepth) || isinf(viewDepth))
        return 0.0;
    float roughness = flooredRoughness(surface.roughness);
    float alpha = roughness * roughness;
    float coneSlope = 2.0 * alpha / max(1.0 - alpha * alpha, 1.0e-6);
    float projectedRadius = coneSlope * (hitDistance / (hitDistance + viewDepth)) / frame.params.z;
    float mirrorLimit = 3.0 * min(alpha / (0.1 * 0.1), 1.0);
    return min(projectedRadius, mirrorLimit) * viewDepth * frame.params.z;
}

ReflectionSharingReceiver reflectionSharingReceiverAt(ivec2 pixel)
{
    float distance = texelFetch(gbufferDistance, pixel, 0).r;
    vec3 point = normalize(viewRayThrough(vec2(pixel) + 0.5)) * distance;
    ShadingInput surface = materialAt(pixel);
    float radius = reflectionSharingRadiusAt(pixel, surface, point);
    float support = clamp(imageLoad(reflectionDistanceHistory, pixel).g * imageLoad(indirectSpecularLight, pixel).a, 0.0, 1.0);
    return ReflectionSharingReceiver(surface, point, distance, radius, support, pixel);
}

bool reflectionSharingReceiversAgree(ReflectionSharingReceiver first, ReflectionSharingReceiver second)
{
    if (first.distance <= 0.0 || second.distance <= 0.0 || isnan(first.distance) || isnan(second.distance)
        || isinf(first.distance) || isinf(second.distance))
        return false;
    ShadingInput surface = first.surface;
    ShadingInput other = second.surface;
    if (surface.model != ShadingStandard || other.model != surface.model || other.metallic != surface.metallic
        || other.reflectance != surface.reflectance || other.roughness != surface.roughness
        || any(notEqual(other.baseColor, surface.baseColor)))
        return false;
    if (dot(surface.geometricNormal, other.geometricNormal) < 0.984807753
        || dot(surface.normal, other.normal) < 0.984807753)
        return false;
    vec3 separation = second.point - first.point;
    float planeError = max(abs(dot(separation, surface.geometricNormal)), abs(dot(separation, other.geometricNormal)));
    float footprint = min(first.distance, second.distance) * frame.params.z;
    float arithmeticFloor = 8.0 * 1.192092896e-7 * max(first.distance, second.distance);
    return planeError <= max(0.02 * footprint, arithmeticFloor);
}

bool reflectionSharingCellAgrees(ivec2 pixel, ReflectionSharingReceiver first, ReflectionSharingReceiver second)
{
    ReflectionSharingReceiver receiver = reflectionSharingReceiverAt(pixel);
    return reflectionSharingReceiversAgree(first, receiver) && reflectionSharingReceiversAgree(second, receiver);
}

bool reflectionSharingConnectionAgrees(ReflectionSharingReceiver first, ReflectionSharingReceiver second)
{
    ivec2 delta = abs(second.pixel - first.pixel);
    ivec2 direction = ivec2(sign(vec2(second.pixel - first.pixel)));
    ivec2 crossed = ivec2(0);
    ivec2 pixel = first.pixel;
    for (int step = 0; step < 4; step++)
    {
        int nextX = delta.x == 0 ? 2147483647 : (2 * crossed.x + 1) * delta.y;
        int nextY = delta.y == 0 ? 2147483647 : (2 * crossed.y + 1) * delta.x;
        if (nextX == nextY)
        {
            if (!reflectionSharingCellAgrees(pixel + ivec2(direction.x, 0), first, second)
                || !reflectionSharingCellAgrees(pixel + ivec2(0, direction.y), first, second))
                return false;
            pixel += direction;
            crossed += ivec2(1);
        }
        else if (nextX < nextY)
        {
            pixel.x += direction.x;
            crossed.x++;
        }
        else
        {
            pixel.y += direction.y;
            crossed.y++;
        }
        if (all(equal(pixel, second.pixel)))
            return true;
        if (!reflectionSharingCellAgrees(pixel, first, second))
            return false;
    }
    return false;
}

vec3 reflectionOutgoingAt(ivec2 pixel)
{
    ShadingInput surface = materialAt(pixel);
    vec3 view = -normalize(viewRayThrough(vec2(pixel) + 0.5));
    MaterialFactors factors = materialFactors(surface, lobesOf(surface, view));
    return factors.specular * imageLoad(indirectSpecularLight, pixel).rgb;
}

vec3 reflectionSharedAt(ivec2 pixel, vec3 outgoing)
{
    if (!reflectionSharingPrototype)
        return outgoing;
    ReflectionSharingReceiver receiver = reflectionSharingReceiverAt(pixel);
    if (receiver.radius <= 0.0)
        return outgoing;
    if (!reflectionSharingReceiversAgree(receiver, receiver) || any(isnan(outgoing)) || any(isinf(outgoing)))
        return outgoing;
    vec3 direction = dominantDirectionAt(pixel);
    vec3 center = reflectionOutgoingAt(pixel);
    vec3 result = center;
    ivec2 size = ivec2(frame.viewport.xy);
    for (int y = -2; y <= 2; y++)
        for (int x = -2; x <= 2; x++)
        {
            ivec2 neighbor = pixel + ivec2(x, y);
            if (x == 0 && y == 0 || any(lessThan(neighbor, ivec2(0))) || any(greaterThanEqual(neighbor, size)))
                continue;
            ReflectionSharingReceiver other = reflectionSharingReceiverAt(neighbor);
            if (length(other.point - receiver.point) > min(receiver.radius, other.radius))
                continue;
            if (!reflectionSharingReceiversAgree(receiver, other) || !reflectionSharingConnectionAgrees(receiver, other))
                continue;
            vec3 value = reflectionOutgoingAt(neighbor);
            if (any(isnan(value)) || any(isinf(value)))
                continue;
            vec3 angularStep = dominantDirectionAt(neighbor) - direction;
            float alpha = min(flooredRoughness(receiver.surface.roughness), flooredRoughness(other.surface.roughness));
            alpha *= alpha;
            float widthSquared = alpha * alpha;
            float weight = (1.0 / 48.0) * widthSquared / (widthSquared + dot(angularStep, angularStep));
            weight *= min(receiver.support, other.support);
            result += weight * (value - center);
        }
    return result;
}

#endif
