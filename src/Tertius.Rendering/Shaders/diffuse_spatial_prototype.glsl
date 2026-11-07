#include "accumulate.glsl"

vec3 positionAt(ivec2 pixel, float distanceToEye)
{
    return normalize(viewRayThrough(vec2(pixel) + 0.5)) * distanceToEye;
}

bool litAt(ivec2 pixel)
{
    return texelFetch(gbufferBaseColor, pixel, 0).a > 0.0;
}

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (!insideImages(pixel))
        return;
    vec4 centerLight = imageLoad(diffusePrototypeInput, pixel);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (distanceToEye <= 0.0 || !litAt(pixel))
    {
        imageStore(diffusePrototypeOutput, pixel, centerLight);
        return;
    }

    vec3 center = positionAt(pixel, distanceToEye);
    vec3 normal = octDecode(texelFetch(gbufferNormal, pixel, 0).rg);
    vec3 axis = abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
    vec3 tangent = normalize(cross(axis, normal));
    vec3 bitangent = cross(normal, tangent);
    float viewCosine = max(abs(dot(normal, normalize(center))), 0.0001);
    float pixelFootprint = tan(frame.params.z) * distanceToEye / viewCosine;
    float distanceRadius = distanceToEye * 0.5 / mix(5.0, 50.0, clamp(distanceToEye / 500.0, 0.0, 1.0));
    float radius = max(distanceRadius, 2.0 * pixelFootprint);
    float centerDepth = dot(center, frame.cameraForward.xyz);
    vec3 lightSum = vec3(0.0);
    float weightSum = 0.0;

    for (int sampleIndex = 0; sampleIndex < 16; sampleIndex++)
    {
        float sampleRadius = radius * float(sampleIndex) / 15.0;
        float angle = float(sampleIndex) * 2.399963229728653;
        vec3 projected = center + sampleRadius * (cos(angle) * tangent + sin(angle) * bitangent);
        float projectedDepth = dot(projected, frame.cameraForward.xyz);
        if (projectedDepth <= 0.0)
            continue;
        vec2 projectedPixel = pixelOf(projected);
        if (any(lessThan(projectedPixel, vec2(0.0))) || any(greaterThanEqual(projectedPixel, frame.viewport.xy)))
            continue;
        ivec2 neighbor = ivec2(projectedPixel);
        float neighborDistance = texelFetch(gbufferDistance, neighbor, 0).r;
        if (neighborDistance <= 0.0 || !litAt(neighbor))
            continue;
        vec3 neighborPosition = positionAt(neighbor, neighborDistance);
        float neighborDepth = dot(neighborPosition, frame.cameraForward.xyz);
        if (abs(neighborDepth - projectedDepth) > 0.1)
            continue;

        vec3 neighborNormal = octDecode(texelFetch(gbufferNormal, neighbor, 0).rg);
        vec3 separation = center - neighborPosition;
        float distanceSquared = dot(separation, separation);
        float planeError = max(abs(dot(separation, normal)), abs(dot(separation, neighborNormal)));
        float planeWeight = distanceSquared < 0.0001 ? 1.0
            : pow(max(0.0, 1.0 - 2.0 * planeError / sqrt(distanceSquared)), 2.0);
        float normalWeight = pow(max(0.0, dot(normal, neighborNormal)), 4.0);
        float depthWeight = max(0.0, 1.0 - abs(neighborDepth - centerDepth) / max(centerDepth, 0.0001));
        float distanceWeight = exp(-pow(sampleRadius / (0.9 * radius), 2.0));
        float weight = sampleRadius > 0.001 ? planeWeight * normalWeight * depthWeight * distanceWeight : 1.0;
        vec3 neighborLight = imageLoad(diffusePrototypeInput, neighbor).rgb;
        if (any(isnan(neighborLight)) || any(isinf(neighborLight)))
            continue;
        lightSum += neighborLight * weight;
        weightSum += weight;
    }

    vec3 filtered = weightSum > 0.0 ? lightSum / weightSum : centerLight.rgb;
    imageStore(diffusePrototypeOutput, pixel, vec4(filtered, centerLight.a));
}
