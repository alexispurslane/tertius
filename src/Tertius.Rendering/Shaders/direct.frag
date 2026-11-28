#version 460
// The direct pass (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 7): the direct light of each lit pixel in its two parts, the
// diffuse and specular radiance of every light of the main view with its shadows, as the forward passes compute
// them. Both images hold actual scene radiance, before exposure and without material-factor division. No ambient
// light and no emission: the composite adds both.
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"

layout(location = 0) out vec4 outDiffuse;   // the diffuse part
layout(location = 1) out vec4 outSpecular;  // the specular part

void main()
{
    // A helper invocation of a 2 × 2 quad can lie past the image's last column or row when the size is odd, and a fetch
    // there is undefined, so every read stays inside the image.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), ivec2(frame.viewport.xy) - 1);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    // Sky and impostors: no direct light. An impostor's light, ambient light included, is baked into its emission.
    if (distanceToEye <= 0.0 || texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
        outDiffuse = vec4(0.0);
        outSpecular = vec4(0.0);
        return;
    }

    ShadingInput s = materialAt(pixel);
    vec3 V = -normalize(s.position);
    SurfaceLobes lobes = lobesOf(s, V);
    DirectTerms direct = directLight(s, lobes, V, ShadowMapsMainView, MainView);
    outDiffuse = vec4(direct.diffuse, 1.0);
    outSpecular = vec4(direct.specular, 1.0);
}
