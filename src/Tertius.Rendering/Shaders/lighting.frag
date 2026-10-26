#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;

void main()
{
    // A helper invocation of a 2 × 2 quad can lie past the image's last column or row when the size is odd, and a fetch
    // there is undefined, so every read stays inside the image.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), textureSize(gbufferDistance, 0) - 1);
    vec3 direction = normalize(inRay);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (distanceToEye <= 0.0)
    {
        outColor = vec4(preExposed(skyRadiance(direction) + starRadiance(direction)), 1.0);
        return;
    }

    // The G-buffer pass pre-exposed the emission.
    vec3 emissive = texelFetch(gbufferEmissive, pixel, 0).rgb;
    // Base color alpha 0 marks an unlit pixel: an impostor, whose lighting is baked into its emission.
    if (texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
        outColor = vec4(emissive, 1.0);
        return;
    }

    // The position comes from the ray and the stored distance, so it does not depend on the projection of the
    // depth range that drew the pixel.
    vec3 position = direction * distanceToEye;
    ShadingInput s = surfaceAt(pixel);
    outColor = vec4(surfaceRadiance(s, emissive, position, -direction, ShadowMapsMainView, exposureScale()), 1.0);
}
