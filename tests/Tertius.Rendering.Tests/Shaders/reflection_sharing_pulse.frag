#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "screen.glsl"
#include "octahedral.glsl"
#include "reflection_distance_guide.glsl"
layout(set = 3, binding = 3, rgba16f) uniform writeonly image2D history;
layout(set = 3, binding = 4, rgba16f) uniform writeonly image2D specularSample;
layout(set = 3, binding = 5, rgba16f) uniform writeonly image2D distanceGuide;
layout(push_constant) uniform TestFixture { int surfaceCase; float hitDistance; float hitSupport; } fixture;
layout(location = 0) out float outDistance;
layout(location = 1) out vec4 outNormal;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    float pulse = all(equal(pixel, ivec2(frame.viewport.xy) / 2)) ? 90.0 : 0.0;
    if (fixture.surfaceCase == 9)
        pulse = 12.0;
    if (fixture.surfaceCase == 10)
        pulse = pixel.x >= int(frame.viewport.x) / 2 ? 90.0 : 0.0;
    imageStore(history, pixel, vec4(vec3(pulse), 1.0));
    int center = int(frame.viewport.x) / 2;
    float hitDistance = fixture.hitDistance;
    if (fixture.surfaceCase == 7 && pixel.x < center)
        hitDistance = 0.0;
    if (fixture.surfaceCase == 8 && pixel.x < center)
        hitDistance = 0.5;
    imageStore(specularSample, pixel, vec4(vec3(0.0), hitDistance));
    if (reflectionSharingPrototype)
        imageStore(distanceGuide, pixel, vec4(reflectionDistanceSample(hitDistance) * fixture.hitSupport, 0.0, 0.0));
    vec3 normal = fixture.surfaceCase == 5 ? vec3(0.5, 0.0, 0.866025404) : vec3(0.0, 0.0, 1.0);
    vec3 geometricNormal = normal;
    float planeDistance = 19.5;
    if (fixture.surfaceCase == 2 && pixel.x == center - 1)
        planeDistance += 1.0;
    if (fixture.surfaceCase == 3 && pixel.x < center)
        planeDistance += 0.02;
    if (fixture.surfaceCase == 4 && pixel.x < center)
        normal = vec3(0.258819045, 0.0, 0.965925826);
    if (fixture.surfaceCase == 6 && pixel.x < center)
        geometricNormal = vec3(0.258819045, 0.0, 0.965925826);
    vec3 ray = normalize(viewRayThrough(vec2(pixel) + 0.5));
    vec3 planeNormal = fixture.surfaceCase == 5 ? geometricNormal : vec3(0.0, 0.0, 1.0);
    outDistance = planeDistance / -dot(ray, planeNormal);
    if (fixture.surfaceCase == 1 && pixel.x == center - 1)
        outDistance = 0.0;
    outNormal = vec4(octEncode(normal), octEncode(geometricNormal));
}
