#version 460
// The direct pass (docs/plans/screen-space-illumination.md, "The direct pass"): the light of the sun and of the point and
// spot lights, plus the emission, pre-exposed and capped, into the direct image. No ambient light: the ambient pass adds
// it after the occlusion passes, and the occlusion passes read this image.
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_surface.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    vec3 direction = normalize(inRay);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    // Sky: no reader reads the direct image there, and the ambient pass draws the star field.
    if (distanceToEye <= 0.0)
    {
        outColor = vec4(0.0);
        return;
    }

    // The G-buffer pass pre-exposed the emission.
    vec3 emissive = texelFetch(gbufferEmissive, pixel, 0).rgb;
    // Base color alpha 0 marks an unlit pixel: an impostor, whose light, ambient light included, is baked into its
    // emission.
    if (texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
        outColor = vec4(emissive, 1.0);
        return;
    }

    // The position comes from the ray and the stored distance, so it does not depend on the projection of the
    // depth range that drew the pixel.
    vec3 position = direction * distanceToEye;
    vec3 V = -direction;
    ShadingInput s = materialAt(pixel);
    vec3 direct = directLight(s, lobesOf(s, V), position, V, true);
    outColor = vec4(min(exposed(direct, exposureScale()) + emissive, vec3(MaxRadiance)), 1.0);
}
