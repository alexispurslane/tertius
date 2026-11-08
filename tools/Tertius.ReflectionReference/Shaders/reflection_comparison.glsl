#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"

#include "specular_history.glsl"
layout(set = 3, binding = 3, SPECULAR_HISTORY_FORMAT) uniform readonly image2D indirectSpecularLight;
layout(location = 0) in vec3 inRay;
#if PROBE_GROUP == 0
layout(location = 0) out vec2 outLight;
layout(location = 1) out vec2 outMaterial;
layout(location = 2) out vec2 outColor;
#elif PROBE_GROUP == 1
layout(location = 0) out vec2 outNormalXY;
layout(location = 1) out vec2 outNormalZDistance;
layout(location = 2) out vec2 outPositionXY;
#else
layout(location = 0) out vec2 outPositionZViewX;
layout(location = 1) out vec2 outViewYZ;
#endif

void main()
{
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), ivec2(frame.viewport.xy) - 1);
    float distance = texelFetch(gbufferDistance, pixel, 0).r;
    ShadingInput s = materialAt(pixel);
    vec3 V = -normalize(inRay);
    MaterialFactors factors = materialFactors(s, lobesOf(s, V));
    vec3 outgoing = factors.specular * imageLoad(indirectSpecularLight, pixel).rgb;
    vec3 position = normalize(viewRayThrough(gl_FragCoord.xy)) * distance;
#if PROBE_GROUP == 0
    outLight = vec2(distance > 0.0 ? outgoing.r : 0.0, texelFetch(gbufferEmissive, pixel, 0).r);
    outMaterial = vec2(s.roughness, s.reflectance);
    outColor = vec2(s.baseColor.r, s.metallic);
#elif PROBE_GROUP == 1
    outNormalXY = s.normal.xy;
    outNormalZDistance = vec2(s.normal.z, distance);
    outPositionXY = position.xy;
#else
    outPositionZViewX = vec2(position.z, V.x);
    outViewYZ = V.yz;
#endif
}
