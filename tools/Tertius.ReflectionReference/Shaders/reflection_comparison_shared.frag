#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"

#include "specular_history.glsl"
layout(set = 3, binding = 3, SPECULAR_HISTORY_FORMAT) uniform readonly image2D indirectSpecularLight;
#include "reflection_sharing_prototype.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec2 outLight;

void main()
{
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), ivec2(frame.viewport.xy) - 1);
    if (texelFetch(gbufferDistance, pixel, 0).r <= 0.0)
    {
        outLight = vec2(0.0);
        return;
    }
    ShadingInput surface = materialAt(pixel);
    vec3 view = -normalize(inRay);
    MaterialFactors factors = materialFactors(surface, lobesOf(surface, view));
    vec3 outgoing = factors.specular * imageLoad(indirectSpecularLight, pixel).rgb;
    outLight = vec2(reflectionSharedAt(pixel, outgoing).r, outgoing.r);
}
