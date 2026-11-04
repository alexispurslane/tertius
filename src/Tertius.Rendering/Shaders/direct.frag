#version 460
// The direct pass (docs/plans/temporal-renderer.md, section 7): the direct light of each lit pixel in its two parts, the
// diffuse and the specular light of every light of the main view with its shadows, as the loop of the forward passes takes
// them, each divided by its material factor and pre-exposed (RenderTargets.DirectDiffuse and DirectSpecular). No ambient
// light and no emission: the composite adds both.
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outDiffuse;   // the diffuse part
layout(location = 1) out vec4 outSpecular;  // the specular part

void main()
{
    // A helper invocation of a 2 × 2 quad can lie past the image's last column or row when the size is odd, and a fetch
    // there is undefined, so every read stays inside the image.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), textureSize(gbufferDistance, 0) - 1);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    // Sky and impostors: no direct light. An impostor's light, ambient light included, is baked into its emission.
    if (distanceToEye <= 0.0 || texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
        outDiffuse = vec4(0.0);
        outSpecular = vec4(0.0);
        return;
    }

    // The position comes from the ray and the stored distance, so it does not depend on the projection of the depth range
    // that drew the pixel.
    vec3 direction = normalize(inRay);
    vec3 position = direction * distanceToEye;
    vec3 V = -direction;
    ShadingInput s = materialAt(pixel);
    SurfaceLobes lobes = lobesOf(s, V);
    DirectTerms direct = directLight(s, lobes, position, V, ShadowMapsMainView, MainView);
    MaterialFactors factors = materialFactors(s, lobes);
    float k = exposureScale();
    outDiffuse = vec4(demodulated(direct.diffuse, factors.diffuse, k), 1.0);
    outSpecular = vec4(demodulated(direct.specular, factors.specular, k), 1.0);
}
