#version 460
// The direct pass under key and fill (docs/plans/light-pairs.md, "Key and fill"): direct.frag with the light of each
// lit pixel from its tile's key lights and fill (key_fill_light.glsl) in place of every light of the list.
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"
#include "key_fill_light.glsl"

layout(location = 0) out vec4 outDiffuse;   // the diffuse part
layout(location = 1) out vec4 outSpecular;  // the specular part

void main()
{
    // As in direct.frag: every read stays inside the image.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), ivec2(frame.viewport.xy) - 1);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (distanceToEye <= 0.0 || texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
        outDiffuse = vec4(0.0);
        outSpecular = vec4(0.0);
        return;
    }

    ShadingInput s = materialAt(pixel);
    vec3 V = -normalize(s.position);
    SurfaceLobes lobes = lobesOf(s, V);
    DirectTerms direct = keyFillLight(s, lobes, V, pixel);
    MaterialFactors factors = materialFactors(s, lobes);
    float k = exposureScale();
    outDiffuse = vec4(demodulated(direct.diffuse, factors.diffuse, k), 1.0);
    outSpecular = vec4(demodulated(direct.specular, factors.specular, k), 1.0);
}
